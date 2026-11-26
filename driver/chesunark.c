/*
 * CHESUN Anti-Rootkit Driver (chesunark.sys)
 *
 * 设计边界：
 *   - 只调用文档化内核 API：ZwUnloadDriver / ZwDeleteKey / ZwDeleteFile。
 *   - 不摘 PsLoadedModuleList，不修改 DriverObject/DriverSection，不摘 SSDT/inline hook，
 *     不修改内核回调，不绕 PatchGuard。
 *   - 因此只能卸载“有 DriverUnload 例程”的驱动；没有 Unload 的 rootkit 需要
 *     本工具用户态“禁用服务 + 重启删除”，或 WinPE 离线包。
 *
 * 设备名：\Device\ChesunArk
 * 符号链接：\DosDevices\ChesunArk
 *
 * IOCTL：
 *   0x00222000  UNLOAD         输入 UTF-16 服务名
 *   0x00222001  DELETE_SERVICE 输入 UTF-16 服务名
 *   0x00222002  DELETE_FILE    输入 UTF-16 NT 路径（例如 \??\C:\Windows\System32\drivers\x.sys）
 */

#include <ntddk.h>
#include <ntstrsafe.h>

#define CHESUN_ARK_DEVICE_NAME L"\\Device\\ChesunArk"
#define CHESUN_ARK_DOS_NAME    L"\\DosDevices\\ChesunArk"
#define CHESUN_ARK_SERVICE_ROOT L"\\Registry\\Machine\\System\\CurrentControlSet\\Services\\"
#define CHESUN_ARK_MAX_NAME 256

#define IOCTL_CHESUN_ARK_UNLOAD \
    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CHESUN_ARK_DELETE_SERVICE \
    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_CHESUN_ARK_DELETE_FILE \
    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_ANY_ACCESS)

static NTSTATUS ChesunBuildServicePath(PCWSTR name, PUNICODE_STRING path, PWCHAR buffer, ULONG chars)
{
    size_t length = 0;
    if (name == NULL) return STATUS_INVALID_PARAMETER;
    if (!NT_SUCCESS(RtlStringCchLengthW(name, CHESUN_ARK_MAX_NAME, &length)) || length == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    NTSTATUS status = RtlStringCchCopyW(buffer, chars, CHESUN_ARK_SERVICE_ROOT);
    if (!NT_SUCCESS(status)) return status;
    status = RtlStringCchCatW(buffer, chars, name);
    if (!NT_SUCCESS(status)) return status;
    RtlInitUnicodeString(path, buffer);
    return STATUS_SUCCESS;
}

static NTSTATUS ChesunUnloadDriverByName(PCWSTR name)
{
    WCHAR buffer[CHESUN_ARK_MAX_NAME + 80];
    UNICODE_STRING servicePath;
    NTSTATUS status = ChesunBuildServicePath(name, &servicePath, buffer, sizeof(buffer) / sizeof(buffer[0]));
    if (!NT_SUCCESS(status)) return status;

    /* ZwUnloadDriver 接收的是服务键路径；驱动必须有 DriverUnload 例程。 */
    return ZwUnloadDriver(&servicePath);
}

static NTSTATUS ChesunDeleteServiceKey(PCWSTR name)
{
    WCHAR buffer[CHESUN_ARK_MAX_NAME + 80];
    UNICODE_STRING servicePath;
    NTSTATUS status = ChesunBuildServicePath(name, &servicePath, buffer, sizeof(buffer) / sizeof(buffer[0]));
    if (!NT_SUCCESS(status)) return status;

    OBJECT_ATTRIBUTES oa;
    InitializeObjectAttributes(&oa, &servicePath, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);
    HANDLE key = NULL;
    status = ZwOpenKey(&key, KEY_ALL_ACCESS, &oa);
    if (!NT_SUCCESS(status)) return status;
    status = ZwDeleteKey(key);
    ZwClose(key);
    return status;
}

static NTSTATUS ChesunDeleteFileByNtPath(PCWSTR ntPath)
{
    size_t length = 0;
    NTSTATUS lengthStatus;
    if (ntPath == NULL) return STATUS_INVALID_PARAMETER;
    lengthStatus = RtlStringCchLengthW(ntPath, 520, &length);
    if (!NT_SUCCESS(lengthStatus) || length == 0) return STATUS_INVALID_PARAMETER;
    UNICODE_STRING path;
    RtlInitUnicodeString(&path, ntPath);
    OBJECT_ATTRIBUTES oa;
    InitializeObjectAttributes(&oa, &path, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);
    return ZwDeleteFile(&oa);
}

static NTSTATUS ChesunCreateClose(PDEVICE_OBJECT device, PIRP irp)
{
    UNREFERENCED_PARAMETER(device);
    irp->IoStatus.Status = STATUS_SUCCESS;
    irp->IoStatus.Information = 0;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

static NTSTATUS ChesunDeviceControl(PDEVICE_OBJECT device, PIRP irp)
{
    UNREFERENCED_PARAMETER(device);
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(irp);
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    ULONG inLength = stack->Parameters.DeviceIoControl.InputBufferLength;
    ULONG code = stack->Parameters.DeviceIoControl.IoControlCode;
    PWCHAR input = (PWCHAR)irp->AssociatedIrp.SystemBuffer;

    if (inLength < sizeof(WCHAR) || inLength > 2048 || input == NULL) {
        status = STATUS_INVALID_PARAMETER;
        goto done;
    }
    input[inLength / sizeof(WCHAR) - 1] = L'\0';
    if (input[0] == L'\0') {
        status = STATUS_INVALID_PARAMETER;
        goto done;
    }

    switch (code) {
    case IOCTL_CHESUN_ARK_UNLOAD:
        status = ChesunUnloadDriverByName(input);
        break;
    case IOCTL_CHESUN_ARK_DELETE_SERVICE:
        status = ChesunDeleteServiceKey(input);
        break;
    case IOCTL_CHESUN_ARK_DELETE_FILE:
        status = ChesunDeleteFileByNtPath(input);
        break;
    default:
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

done:
    irp->IoStatus.Status = status;
    irp->IoStatus.Information = 0;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return status;
}

static VOID ChesunUnload(PDRIVER_OBJECT driver)
{
    UNICODE_STRING dosName;
    RtlInitUnicodeString(&dosName, CHESUN_ARK_DOS_NAME);
    IoDeleteSymbolicLink(&dosName);
    if (driver->DeviceObject != NULL) IoDeleteDevice(driver->DeviceObject);
}

NTSTATUS DriverEntry(PDRIVER_OBJECT driver, PUNICODE_STRING registryPath)
{
    UNREFERENCED_PARAMETER(registryPath);
    UNICODE_STRING devName, dosName;
    PDEVICE_OBJECT device = NULL;
    NTSTATUS status;

    RtlInitUnicodeString(&devName, CHESUN_ARK_DEVICE_NAME);
    status = IoCreateDevice(driver, 0, &devName, FILE_DEVICE_UNKNOWN,
        FILE_DEVICE_SECURE_OPEN, FALSE, &device);
    if (!NT_SUCCESS(status)) return status;

    RtlInitUnicodeString(&dosName, CHESUN_ARK_DOS_NAME);
    status = IoCreateSymbolicLink(&dosName, &devName);
    if (!NT_SUCCESS(status)) {
        IoDeleteDevice(device);
        return status;
    }

    driver->MajorFunction[IRP_MJ_CREATE] = ChesunCreateClose;
    driver->MajorFunction[IRP_MJ_CLOSE] = ChesunCreateClose;
    driver->MajorFunction[IRP_MJ_DEVICE_CONTROL] = ChesunDeviceControl;
    driver->DriverUnload = ChesunUnload;

    DbgPrint("CHESUN Ark: loaded, device %ws\n", CHESUN_ARK_DEVICE_NAME);
    return STATUS_SUCCESS;
}

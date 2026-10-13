using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;

namespace GroceryPOS.Application.Shifts;

public static class ShiftErrors
{
    public static readonly Error NoOpenShift = Error.Conflict("Bạn chưa mở ca. Vui lòng mở ca trước khi thao tác.");

    public static readonly Error ShiftNotFound = Error.NotFound("Không tìm thấy ca làm việc.");

    public static readonly Error AlreadyClosed = Error.Conflict("Ca đã kết thúc.");

    public static readonly Error NotYourShift = Error.Forbidden("Bạn chỉ được xem và kết ca của chính mình.");

    public static readonly Error CountMissing = Error.Validation("Vui lòng nhập số tiền mặt thực đếm.");

    public static Error AlreadyOpen(string code) => Error.Conflict($"Bạn đang có ca {code} chưa kết thúc.");

    public static Error CountMismatch(decimal typed, decimal counted) =>
        Error.Validation($"Tổng theo mệnh giá ({Vnd.Format(counted)}) khác số tiền đã nhập ({Vnd.Format(typed)}).");

    public static Error InsufficientCash(decimal available) =>
        Error.Conflict($"Không đủ tiền mặt trong két (hiện có {Vnd.Format(available)}).");
}

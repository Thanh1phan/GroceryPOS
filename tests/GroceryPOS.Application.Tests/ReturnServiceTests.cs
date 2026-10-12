using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

public class ReturnServiceTests
{
    private readonly InMemorySaleRepository _sales = new();
    private readonly InMemorySaleReturnRepository _returns = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 09:00 in Vietnam
    private readonly Product _milk;
    private readonly Product _noodles;
    private readonly Customer _member;

    public ReturnServiceTests()
    {
        _milk = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", costPrice: 28_000, sellPrice: 35_000, VatRate.Five);
        _milk.AdjustStock(20);
        _products.Add(_milk);

        _noodles = new Product("MI01", "Mì Hảo Hảo tôm chua cay", 1, "gói", costPrice: 3_500, sellPrice: 5_000, VatRate.Ten);
        _noodles.AdjustStock(100);
        _products.Add(_noodles);

        _member = new Customer("KH001", "Nguyễn Văn A", "0901234567");
        _customers.Add(_member);
    }

    private ReturnService CreateSut() =>
        new(_sales, _returns, _products, _movements, _customers, _uow, _session, _audit, _clock);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(7, "thungan", "Trần Thị B", SystemRoles.Cashier, permissions.ToHashSet(), _clock.Now));

    /// <summary>Milk 2 × 35.000 + noodles 10 × 5.000 = 120.000 paid cash, optionally for the member (12 points).</summary>
    private async Task<Sale> SellAsync(bool member = false)
    {
        SignIn(Permissions.SalesCreate);
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);
        var result = await checkout.CheckoutAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_milk.Id, 2), new CheckoutLine(_noodles.Id, 10) },
            new[] { new PaymentInput(PaymentMethod.Cash, 120_000) },
            CustomerId: member ? _member.Id : null));
        Assert.True(result.IsSuccess);
        SignIn(Permissions.ReturnsCreate);
        return _sales.All.Single(s => s.Id == result.Value.SaleId);
    }

    private static int LineOf(Sale sale, Product product) => sale.Lines.Single(l => l.ProductId == product.Id).Id;

    [Fact]
    public async Task Return_refunds_restocks_writes_ledger_and_audits()
    {
        var sale = await SellAsync();

        var result = await CreateSut().CreateAsync(new CreateReturnRequest(
            sale.Id, new[] { new ReturnLineInput(LineOf(sale, _milk), 1) }, "Hộp bị móp"));

        Assert.True(result.IsSuccess);
        var receipt = result.Value;
        Assert.Equal("TH261008-0001", receipt.Code);
        Assert.Equal(sale.Code, receipt.SaleCode);
        Assert.Equal(35_000m, receipt.RefundTotal);
        Assert.False(receipt.SaleFullyReturned);
        Assert.Equal(19m, _milk.StockQuantity);

        var movement = _movements.All.Single(m => m.Type == StockMovementType.CustomerReturn);
        Assert.Equal(1m, movement.Quantity);
        Assert.Equal("TH261008-0001", movement.ReferenceCode);
        Assert.Equal(28_000m, movement.UnitCost);
        Assert.Contains(AuditActions.SaleReturned, _audit.Actions);
        Assert.Single(_returns.All);
    }

    [Fact]
    public async Task Written_off_goods_are_refunded_but_not_restocked()
    {
        var sale = await SellAsync();

        var result = await CreateSut().CreateAsync(new CreateReturnRequest(
            sale.Id, new[] { new ReturnLineInput(LineOf(sale, _noodles), 3, Restock: false) }, "Hàng hết hạn"));

        Assert.True(result.IsSuccess);
        Assert.Equal(15_000m, result.Value.RefundTotal);
        Assert.Equal(10_500m, result.Value.WrittenOffCost);
        Assert.Equal(90m, _noodles.StockQuantity);
        Assert.False(_movements.All.Any(m => m.Type == StockMovementType.CustomerReturn));
    }

    [Fact]
    public async Task Member_loses_points_and_spend_for_returned_goods()
    {
        var sale = await SellAsync(member: true);
        Assert.Equal(12, _member.LoyaltyPoints);

        var result = await CreateSut().CreateAsync(new CreateReturnRequest(
            sale.Id, new[] { new ReturnLineInput(LineOf(sale, _milk), 2) }, "Khách đổi ý", PaymentMethod.BankTransfer));

        Assert.True(result.IsSuccess);
        Assert.Equal(70_000m, result.Value.RefundTotal);
        Assert.Equal(7, result.Value.PointsReversed); // floor(12 × 70/120)
        Assert.Equal(5, result.Value.CustomerPointsBalance);
        Assert.Equal(50_000m, _member.TotalSpent);
        Assert.Equal(PaymentMethod.BankTransfer, result.Value.RefundMethod);
    }

    [Fact]
    public async Task Codes_are_sequential_and_full_return_is_detected()
    {
        var sale = await SellAsync();
        var sut = CreateSut();

        var first = await sut.CreateAsync(new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(LineOf(sale, _milk), 2) }, "Lỗi"));
        var second = await sut.CreateAsync(new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(LineOf(sale, _noodles), 10) }, "Lỗi"));
        var third = await sut.CreateAsync(new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(LineOf(sale, _noodles), 1) }, "Lỗi"));

        Assert.Equal("TH261008-0001", first.Value.Code);
        Assert.Equal("TH261008-0002", second.Value.Code);
        Assert.True(second.Value.SaleFullyReturned);
        Assert.True(third.IsFailure);
        Assert.Equal(ReturnErrors.AlreadyFullyReturned, third.Error);

        var history = await sut.ListBySaleAsync(sale.Id);
        Assert.Equal(2, history.Value.Count);
        Assert.Equal(120_000m, history.Value.Sum(r => r.RefundTotal));
    }

    [Fact]
    public async Task Too_large_quantity_fails_without_side_effects()
    {
        var sale = await SellAsync();
        var saves = _uow.SaveCount;

        var result = await CreateSut().CreateAsync(new CreateReturnRequest(
            sale.Id,
            new[] { new ReturnLineInput(LineOf(sale, _milk), 1), new ReturnLineInput(LineOf(sale, _noodles), 11) },
            "Lỗi"));

        Assert.True(result.IsFailure);
        Assert.Equal("return.quantity", result.Error.Code);
        Assert.False(sale.HasReturns);
        Assert.Equal(18m, _milk.StockQuantity);
        Assert.Empty(_returns.All);
        Assert.Equal(saves, _uow.SaveCount);
    }

    [Fact]
    public async Task Unknown_line_and_empty_request_are_rejected()
    {
        var sale = await SellAsync();
        var sut = CreateSut();

        var unknown = await sut.CreateAsync(new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(999, 1) }, "Lỗi"));
        var empty = await sut.CreateAsync(new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(LineOf(sale, _milk), 0) }, "Lỗi"));

        Assert.Equal("not_found", unknown.Error.Code);
        Assert.Equal(ReturnErrors.EmptyReturn, empty.Error);
    }

    [Fact]
    public async Task Requires_returns_permission()
    {
        var sale = await SellAsync();
        SignIn(Permissions.SalesCreate);

        var result = await CreateSut().CreateAsync(new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(LineOf(sale, _milk), 1) }, "Lỗi"));

        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Old_invoice_needs_manager_permission()
    {
        var sale = await SellAsync();
        _clock.Advance(TimeSpan.FromDays(8));
        var request = new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(LineOf(sale, _milk), 1) }, "Lỗi");

        var asCashier = await CreateSut().CreateAsync(request);
        SignIn(Permissions.ReturnsCreate, Permissions.SalesVoid);
        var asManager = await CreateSut().CreateAsync(request);

        Assert.Equal("forbidden", asCashier.Error.Code);
        Assert.True(asManager.IsSuccess);
    }

    [Fact]
    public async Task Return_window_counts_store_calendar_days()
    {
        var sale = await SellAsync(); // 09:00 on 08/10
        _clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromHours(12)); // 21:00 on 15/10

        var returnable = await CreateSut().GetReturnableAsync(sale.Code.ToLowerInvariant());

        Assert.True(returnable.IsSuccess);
        Assert.True(returnable.Value.IsWithinReturnWindow);
        Assert.True(returnable.Value.CanReturn);
    }

    [Fact]
    public async Task Returnable_view_shows_remaining_quantities_and_refunds()
    {
        var sale = await SellAsync(member: true);
        var sut = CreateSut();
        await sut.CreateAsync(new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(LineOf(sale, _noodles), 4) }, "Lỗi"));

        var result = await sut.GetReturnableAsync(sale.Code);

        Assert.True(result.IsSuccess);
        var dto = result.Value;
        Assert.Equal("Nguyễn Văn A", dto.CustomerName);
        Assert.Equal(20_000m, dto.RefundedTotal);
        var noodles = dto.Lines.Single(l => l.ProductId == _noodles.Id);
        Assert.Equal(6m, noodles.RemainingQuantity);
        Assert.Equal(30_000m, noodles.RemainingRefund);
        Assert.Equal(5_000m, noodles.UnitRefund);
    }

    [Fact]
    public async Task Voided_invoice_cannot_be_returned()
    {
        var sale = await SellAsync();
        sale.Void(_clock.Now, 1, "Nhập sai");

        var result = await CreateSut().GetReturnableAsync(sale.Code);

        Assert.Equal(ReturnErrors.SaleVoided, result.Error);
    }

    [Fact]
    public async Task Invoice_with_returns_can_no_longer_be_voided()
    {
        var sale = await SellAsync();
        await CreateSut().CreateAsync(new CreateReturnRequest(sale.Id, new[] { new ReturnLineInput(LineOf(sale, _milk), 1) }, "Lỗi"));
        SignIn(Permissions.SalesVoid);
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);

        var result = await checkout.VoidAsync(sale.Id, "Hủy");

        Assert.Equal("sale.void", result.Error.Code);
        Assert.Equal(19m, _milk.StockQuantity);
    }
}

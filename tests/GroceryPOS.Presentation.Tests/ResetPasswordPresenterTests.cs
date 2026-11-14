using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Users;

namespace GroceryPOS.Presentation.Tests;

public sealed class ResetPasswordPresenterTests
{
    private sealed class FakeResetView : RecordingView, IResetPasswordView
    {
        public string Password { get; set; } = string.Empty;
    }

    private static (TestHost Host, ResetPasswordPresenter Presenter, FakeResetView View) Create()
    {
        var host = new TestHost(Permissions.UsersManage);
        var presenter = host.Get<ResetPasswordPresenter>().For(UserListPresenterTests.Lan);
        var view = new FakeResetView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Shows_the_account()
    {
        var (host, presenter, _) = Create();
        using var _h = host;
        Assert.Equal("thungan1 – Nguyễn Thị Lan", presenter.AccountText);
        Assert.Throws<InvalidOperationException>(() => host.Get<ResetPasswordPresenter>().User);
    }

    [Fact]
    public async Task Resets_and_shows_the_new_password()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.Password = UserEditPresenter.NewTemporaryPassword();

        Assert.Null(await presenter.SaveAsync());

        Assert.Equal(new[] { $"reset:2:{view.Password}" }, host.Users.Calls);
        Assert.Contains(view.Password, Assert.Single(view.Infos));
    }

    [Fact]
    public async Task Weak_password_and_service_errors_go_to_the_banner()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.Password = "1";
        Assert.NotNull(await presenter.SaveAsync());
        Assert.Empty(host.Users.Calls);

        view.Password = UserEditPresenter.NewTemporaryPassword();
        host.Users.FailWith = Error.Forbidden();
        Assert.Equal(Error.Forbidden().Message, await presenter.SaveAsync());
        Assert.Empty(view.Infos);
    }
}

using BreakdownReport.Data;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly PasswordAuthService _auth;

    public DictionaryStore Store { get; }

    [BindProperty] public int EmployeeId { get; set; }
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; set; }

    public LoginModel(PasswordAuthService auth, DictionaryStore store)
    {
        _auth = auth;
        Store = store;
    }

    public void OnGet() { }

    public IActionResult OnPost(string? returnUrl)
    {
        if (EmployeeId == 0 || !_auth.Verify(EmployeeId, Password))
        {
            Error = "Неверный сотрудник или пароль";
            return Page();
        }

        Response.Cookies.Append("br_session", _auth.CreateToken(EmployeeId), new CookieOptions
        {
            Expires = DateTimeOffset.Now.AddYears(1),
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax
        });
        Response.Cookies.Append("currentUserId", EmployeeId.ToString(), new CookieOptions
        {
            Expires = DateTimeOffset.Now.AddYears(1),
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax
        });

        var isLocal = !string.IsNullOrEmpty(returnUrl)
                      && returnUrl.StartsWith("/")
                      && !returnUrl.StartsWith("//")
                      && !returnUrl.StartsWith("/\\");
        return Redirect(isLocal ? returnUrl! : "/");
    }
}
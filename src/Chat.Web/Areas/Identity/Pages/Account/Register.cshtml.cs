using System.ComponentModel.DataAnnotations;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Areas.Identity.Pages.Account;

/// <summary>
/// Replaces the default Identity UI registration page for one reason:
/// <see cref="ApplicationUser.DisplayName"/> is required, and the stock page cannot capture it — a
/// participant registered through it would post under a blank name for the rest of the demo.
/// </summary>
/// <remarks>
/// Everything else Identity ships (login, logout, account management) is used untouched. Identity's
/// <see cref="UserManager{TUser}"/> API takes no <see cref="CancellationToken"/>, which is why the
/// handler below has none to forward.
/// </remarks>
/// <param name="userManager">Creates the user and enforces the password policy.</param>
/// <param name="signInManager">Signs the new participant in straight after registration.</param>
/// <param name="logger">Records registrations as a lifecycle event.</param>
[AllowAnonymous]
public sealed class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<RegisterModel> logger) : PageModel
{
    /// <summary>The submitted registration form.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Where to send the participant once registered; defaults to the chat page.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Renders the empty form.</summary>
    /// <param name="returnUrl">Page the participant was trying to reach before being redirected here.</param>
    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    /// <summary>Creates the account, then signs the participant in.</summary>
    /// <param name="returnUrl">Page the participant was trying to reach before being redirected here.</param>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ApplicationUser user = new()
        {
            UserName = Input.Email,
            Email = Input.Email,

            // The whole reason this page exists. Trimmed here so the name stored in AspNetUsers is the
            // name copied onto every post.
            DisplayName = Input.DisplayName.Trim(),
        };

        IdentityResult result = await userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            // Identity's own messages, so the password policy explains itself instead of being restated.
            foreach (IdentityError error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("A new participant registered an account with a password.");

        await signInManager.SignInAsync(user, isPersistent: false);

        return LocalRedirect(returnUrl ?? Url.Content("~/Chat"));
    }

    /// <summary>The registration form. Bound and validated before any user is created.</summary>
    public sealed class InputModel
    {
        /// <summary>
        /// The name shown next to this participant's posts. Bounded to the column width so an
        /// over-long name is a validation message rather than a failed insert.
        /// </summary>
        [Required]
        [StringLength(ApplicationUser.DisplayNameMaxLength, MinimumLength = 1)]
        [Display(Name = "Display name")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Doubles as the Identity user name, which is what the login page authenticates with.</summary>
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Deliberately carries no length rules: the framework password policy is the single source of
        /// truth, and restating part of it here would let the two drift apart.
        /// </summary>
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        /// <summary>Guards against a typo in <see cref="Password"/>.</summary>
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}

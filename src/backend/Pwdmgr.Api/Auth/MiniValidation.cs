using System.ComponentModel.DataAnnotations;

namespace Pwdmgr.Api.Auth;

/// <summary>DataAnnotations validation for minimal-API request records (system boundary only, Constitution §2.5).</summary>
internal static class MiniValidation
{
    public static bool TryValidate(object model, out Dictionary<string, string[]> errors)
    {
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty(string.Empty).Select(m => (Member: m, Message: r.ErrorMessage ?? "invalid")))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Message).ToArray());
        return valid;
    }
}

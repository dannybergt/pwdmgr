namespace Pwdmgr.Api.Vaults;

/// <summary>Base64 (standard alphabet) at the system boundary; bytes inside. Errors name the field, never the content.</summary>
internal static class Base64Field
{
    public static bool TryDecode(string? text, int minLength, int maxLength, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(text) || text.Length > (maxLength * 4 / 3) + 4)
        {
            return false;
        }

        var buffer = new byte[(text.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(text, buffer, out var written) || written < minLength || written > maxLength)
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}

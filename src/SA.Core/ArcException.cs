namespace SA.Core;

/// <summary>Коди відмови з розділу 5 FORMAT_SPEC_P2.</summary>
public static class ErrorCodes
{
    public const string Short = "E_SHORT";
    public const string Magic = "E_MAGIC";
    public const string Length = "E_LENGTH";
    public const string Hmac = "E_HMAC";
    public const string Payload = "E_PAYLOAD";
    public const string Code = "E_CODE";
    public const string Crc = "E_CRC";
    public const string Volumes = "E_VOLUMES";
}

/// <summary>Єдиний тип помилки формату. Поле Code — один із ErrorCodes.</summary>
public sealed class ArcException : Exception
{
    public string Code { get; }

    public ArcException(string code, string message) : base($"{code}: {message}")
    {
        Code = code;
    }
}

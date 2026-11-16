namespace SA.Core;

/// <summary>Коди відмови з розділу 5 FORMAT_SPEC_P2.md.</summary>
public enum ArchiveErrorCode
{
    /// <summary>Том коротший за 73 байти.</summary>
    E_SHORT,

    /// <summary>Невірний magic або version.</summary>
    E_MAGIC,

    /// <summary>Довжина тому не відповідає ciphertext_len.</summary>
    E_LENGTH,

    /// <summary>HMAC не збігається: зіпсовані дані, зіпсований тег або невірний пароль.</summary>
    E_HMAC,

    /// <summary>Ненульове reserved, невідомі біти flags, file_type &gt; 6, неузгоджені довжини секцій, зайві байти, небезпечне ім'я.</summary>
    E_PAYLOAD,

    /// <summary>Довжина коду &gt; 15, порушена рівність Крафта, символ поза таблицею, посилання за межі буфера, ненульове доповнення.</summary>
    E_CODE,

    /// <summary>CRC32 склеєного файла не збігається із записаним.</summary>
    E_CRC,

    /// <summary>Набір томів неповний, містить дублікати або томи з різних архівів.</summary>
    E_VOLUMES,
}

/// <summary>
/// Єдиний виняток для «очікуваних» помилок читання архіву. Усі модулі мають перетворювати
/// внутрішні винятки на цей тип, щоб у логу повного прогону корпусу не було необроблених
/// винятків (критерії C2 і C5).
/// </summary>
public sealed class ArchiveException : Exception
{
    public ArchiveErrorCode Code { get; }

    public ArchiveException(ArchiveErrorCode code, string message, Exception? inner = null)
        : base($"{code}: {message}", inner)
        => Code = code;
}

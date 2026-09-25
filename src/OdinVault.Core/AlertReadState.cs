namespace OdinVault.Core;

public sealed class AlertReadState
{
    public string AlertKey { get; set; } = string.Empty;
    public DateTime ReadAtUtc { get; set; } = DateTime.UtcNow;
}

namespace WebApi.Media;

public sealed class ProductMediaOptions
{
    public string Provider { get; set; } = "Local";
    public string RootPath { get; set; } = "";
    public string SftpExecutable { get; set; } = "sftp";
    public string SshHost { get; set; } = "";
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = "";
    public string SshKeyPath { get; set; } = "";
    public string AgentEnvironmentFile { get; set; } = "";
}

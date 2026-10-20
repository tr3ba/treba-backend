namespace AdminPanel.State;
public sealed record EditorBackupPhoto(Guid Id, string Name, string ContentType, byte[] Bytes);
public sealed record EditorRecovery(string ModelJson, List<EditorBackupPhoto> Photos);

namespace Application.Abstractions;

public interface ICurrentActor
{
    Guid? UserId { get; }
    string Name { get; }
    string Role { get; }
}

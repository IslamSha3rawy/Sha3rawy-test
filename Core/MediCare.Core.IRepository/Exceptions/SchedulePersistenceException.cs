namespace MediCare.Core.IRepository.Exceptions;

/// <summary>Persistence could not save a generated schedule. Details stay in the inner exception.</summary>
public sealed class SchedulePersistenceException : Exception
{
    public SchedulePersistenceException(Exception innerException)
        : base("The generated schedule could not be saved.", innerException) { }
}

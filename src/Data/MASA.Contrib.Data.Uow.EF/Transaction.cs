using System.Text.Json.Serialization;

namespace MASA.Contrib.Data.Uow;

public class Transaction : ITransaction
{
    public Transaction(IUnitOfWork unitOfWork) => UnitOfWork = unitOfWork;

    [JsonIgnore]
    public IUnitOfWork UnitOfWork { get; set; }
}
using RetailPOS.API.DTOs.Transaction;

namespace RetailPOS.API.Services;

public interface ITransactionService
{
    Task<TransactionListDto> SearchAsync(TransactionSearchDto searchDto);
    Task<TransactionDto> GetByIdAsync(long id);
    Task<TransactionDto> CreateAsync(CreateTransactionDto dto);
    Task<IEnumerable<TransactionDto>> GetLedgerAsync(long accountId);
}

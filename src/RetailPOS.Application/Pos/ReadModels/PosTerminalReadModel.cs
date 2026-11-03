namespace RetailPOS.Application.Pos.ReadModels;

public sealed record PosTerminalReadModel(
    long Id,
    string Name,
    string Code,
    bool IsDefault);

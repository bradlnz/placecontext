using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class EfCrmCommunicationRepository : ICrmCommunicationRepository
{
    private readonly CrmDbContext _db;
    private readonly IDataEncryptor _encryptor;

    public EfCrmCommunicationRepository(CrmDbContext db, IDataEncryptor encryptor)
        => (_db, _encryptor) = (db, encryptor);

    public async Task AddAsync(CrmCommunication communication, CancellationToken ct = default)
        => await _db.CrmCommunications.AddAsync(ToRow(communication), ct);

    public async Task UpdateAsync(CrmCommunication communication, CancellationToken ct = default)
    {
        var row = await _db.CrmCommunications.FindAsync(new object[] { communication.Id }, ct);
        if (row is null) return;
        row.Status = communication.Status.ToString();
        row.Provider = communication.Provider;
        row.ExternalId = Protect(communication.ExternalId);
        row.ErrorProtected = Protect(communication.Error);
        row.SentAt = communication.SentAt;
    }

    public async Task<IReadOnlyList<CrmCommunication>> ListForClientAsync(
        Guid clientId,
        int take = 100,
        CancellationToken ct = default)
        => (await _db.CrmCommunications.AsNoTracking()
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct))
            .Select(ToDomain)
            .ToList();

    private CrmCommunicationRow ToRow(CrmCommunication value) => new()
    {
        Id = value.Id,
        ProjectId = value.ProjectId,
        ClientId = value.ClientId,
        Channel = value.Channel.ToString(),
        SubjectProtected = Protect(value.Subject),
        BodyProtected = _encryptor.Protect(value.Body, DataEncryptionPurpose.CrmCommunication),
        RecipientProtected = Protect(value.Recipient),
        Status = value.Status.ToString(),
        Provider = value.Provider,
        ExternalId = Protect(value.ExternalId),
        ErrorProtected = Protect(value.Error),
        CreatedByUserId = value.CreatedByUserId,
        CreatedAt = value.CreatedAt,
        SentAt = value.SentAt,
    };

    private CrmCommunication ToDomain(CrmCommunicationRow row)
        => CrmCommunication.Rehydrate(
            row.Id,
            row.ProjectId,
            row.ClientId,
            Enum.TryParse<CrmCommunicationChannel>(row.Channel, out var channel)
                ? channel : CrmCommunicationChannel.Note,
            Unprotect(row.SubjectProtected),
            _encryptor.Unprotect(row.BodyProtected, DataEncryptionPurpose.CrmCommunication),
            Unprotect(row.RecipientProtected),
            Enum.TryParse<CrmCommunicationStatus>(row.Status, out var status)
                ? status : CrmCommunicationStatus.Added,
            row.Provider,
            Unprotect(row.ExternalId),
            Unprotect(row.ErrorProtected),
            row.CreatedByUserId,
            row.CreatedAt,
            row.SentAt);

    private string? Protect(string? value)
        => value is null ? null : _encryptor.Protect(value, DataEncryptionPurpose.CrmCommunication);

    private string? Unprotect(string? value)
        => value is null ? null : _encryptor.Unprotect(value, DataEncryptionPurpose.CrmCommunication);
}

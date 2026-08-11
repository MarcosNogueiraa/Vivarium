using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Services;

/// <summary>
/// VIP (CLAUDE.md 8.11-adjacente): pacotes de dias pagos em premium, sem renovação
/// automática (VipConfig). Comprar com uma assinatura já ativa ESTENDE o EndAt em vez
/// de sobrescrever — nunca desperdiça dias já pagos.
/// </summary>
public class VipService(VivariumDbContext db)
{
    public async Task<ServiceResult> StatusAsync(long userId, DateTime now)
    {
        var active = await ActiveSubscriptionAsync(userId, now);
        return ServiceResult.Success(new VipStatusDto(
            active is not null, active?.EndAt,
            VipConfig.PackagePricePremium.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value)));
    }

    public async Task<ServiceResult> SubscribeAsync(long userId, int days, DateTime now)
    {
        if (!VipCalculator.TryGetPrice(days, out decimal cost))
            return ServiceResult.Bad("Pacote inválido — escolha 7, 15 ou 30 dias");

        int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId);
        if (wallet.Amount < cost)
            return ServiceResult.Bad("Saldo de moeda premium insuficiente");

        wallet.Amount -= cost;

        var active = await ActiveSubscriptionAsync(userId, now);
        if (active is not null)
        {
            active.EndAt = active.EndAt.AddDays(days);
        }
        else
        {
            active = new VipSubscription { UserId = userId, StartAt = now, EndAt = now.AddDays(days), Status = SubscriptionStatus.Active };
            db.VipSubscriptions.Add(active);
        }

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.VipPurchase,
            FromUserId = userId,
            CurrencyTypeId = premiumId,
            Amount = cost,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Operação concorrente — tente de novo.");
        }
        return ServiceResult.Success(new { paid = cost, endAt = active.EndAt });
    }

    private Task<VipSubscription?> ActiveSubscriptionAsync(long userId, DateTime now) =>
        db.VipSubscriptions
            .Where(v => v.UserId == userId && v.Status == SubscriptionStatus.Active && v.EndAt > now)
            .OrderByDescending(v => v.EndAt)
            .FirstOrDefaultAsync();
}

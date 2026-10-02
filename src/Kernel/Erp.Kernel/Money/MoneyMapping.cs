using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Kernel.Money;

public static class MoneyMapping
{
    /// <summary>
    /// Map a <see cref="Money"/> property as four columns: <c>{prefix}_amount numeric(19,4)</c>,
    /// <c>{prefix}_currency char(3)</c>, <c>{prefix}_exchange_rate numeric(19,8)</c> and
    /// <c>{prefix}_base_amount numeric(19,4)</c>.
    /// </summary>
    public static EntityTypeBuilder<TEntity> MoneyProperty<TEntity>(
        this EntityTypeBuilder<TEntity> entity, Expression<Func<TEntity, Money>> property, string columnPrefix)
        where TEntity : class
    {
        entity.ComplexProperty(property, money =>
        {
            money.Property(m => m.Amount).HasColumnName($"{columnPrefix}_amount")
                .HasPrecision(Money.AmountPrecision, Money.AmountScale).IsRequired();
            money.Property(m => m.Currency).HasColumnName($"{columnPrefix}_currency")
                .HasColumnType("char(3)").IsFixedLength().HasMaxLength(3).IsRequired();
            money.Property(m => m.ExchangeRate).HasColumnName($"{columnPrefix}_exchange_rate")
                .HasPrecision(Money.RatePrecision, Money.RateScale).IsRequired();
            money.Property(m => m.BaseAmount).HasColumnName($"{columnPrefix}_base_amount")
                .HasPrecision(Money.AmountPrecision, Money.AmountScale).IsRequired();
        });
        return entity;
    }
}

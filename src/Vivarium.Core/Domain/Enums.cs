namespace Vivarium.Core.Domain;

public enum SubscriptionStatus { Active, Expired, Cancelled }

public enum QueueItemStatus { Pending, Collected }

public enum ListingStatus { Active, Sold, Cancelled }

public enum TransactionType { MarketSale, DirectTransfer, CurrencyPurchase, ItemPurchase, Sink, Breeding, BreedingLoss, DailyReward, TimeSkip, VendorSale, BreedingInsurance }

public enum BreedingStatus { InProgress, Collected }

public enum ItemCategory { Filter, AutoFilter, HabitatUpgrade }

/// <summary>Partes configuráveis no TraitWeightConfig (inclui Body, diferente do PartType de renderização).</summary>
public enum TraitPartType { Body, Tail, Dorsal, Pectoral }

public enum TraitCategory { ShimmerTier, BaseColor, PatternType, PatternSize, PatternColor, PatternOpacity }

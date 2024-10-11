using Bencodex.Types;
using Lib9c.Models.Exceptions;
using Lib9c.Models.Extensions;
using Libplanet.Crypto;
using Mimir.MongoDB;
using Mimir.MongoDB.Bson;
using Mimir.Worker.CollectionUpdaters;
using Mimir.Worker.Services;
using MongoDB.Driver;
using Nekoyume;
using Nekoyume.Extensions;
using Nekoyume.Model.EnumType;
using Nekoyume.TableData;
using Serilog;

namespace Mimir.Worker.ActionHandler;

public class RaidHandler(IStateService stateService, MongoDbService store)
    : BaseActionHandler(stateService, store, "^raid[0-9]*$", Log.ForContext<RaidHandler>())
{
    protected override async Task HandleAction(
        long blockIndex,
        Address signer,
        IValue actionPlainValue,
        string actionType,
        IValue? actionPlainValueInternal,
        IClientSessionHandle? session = null,
        CancellationToken stoppingToken = default)
    {
        if (actionPlainValueInternal is null)
        {
            throw new ArgumentNullException(nameof(actionPlainValueInternal));
        }

        if (actionPlainValueInternal is not Dictionary d)
        {
            throw new UnsupportedArgumentTypeException<ValueKind>(
                nameof(actionPlainValueInternal),
                [ValueKind.Dictionary],
                actionPlainValueInternal.Kind);
        }

        var avatarAddress = d["a"].ToAddress();
        var equipmentIds = d["e"].ToList(StateExtensions.ToGuid);
        var costumeIds = d["c"].ToList(StateExtensions.ToGuid);
        Logger.Information("Handle raid, avatar: {AvatarAddress}", avatarAddress);

        await ItemSlotCollectionUpdater.UpdateAsync(
            StateService,
            Store,
            BattleType.Raid,
            avatarAddress,
            costumeIds,
            equipmentIds,
            session,
            stoppingToken);

        var worldBossListSheet = await Store.GetSheetAsync<WorldBossListSheet>(stoppingToken);
        if (worldBossListSheet is null)
        {
            throw new InvalidOperationException($"{nameof(RaidHandler)} requires ${nameof(WorldBossListSheet)}");
        }

        var row = worldBossListSheet.FindRowByBlockIndex(blockIndex);
        var raidId = row.Id;
        var worldBossAddress = Addresses.GetWorldBossAddress(raidId);
        var worldBossState = await StateGetter.GetWorldBossStateAsync(worldBossAddress, stoppingToken);
        await Store.UpsertStateDataManyAsync(
            CollectionNames.GetCollectionName<WorldBossStateDocument>(),
            [new WorldBossStateDocument(worldBossAddress, raidId, worldBossState)],
            session,
            stoppingToken);

        var raiderAddress = Addresses.GetRaiderAddress(avatarAddress, raidId);
        var raiderState = await StateGetter.GetRaiderStateAsync(raiderAddress, stoppingToken);
        await Store.UpsertStateDataManyAsync(
            CollectionNames.GetCollectionName<RaiderStateDocument>(),
            [new RaiderStateDocument(raiderAddress, raiderState)],
            session,
            stoppingToken);

        var worldBossKillRewardRecordAddress = Addresses.GetWorldBossKillRewardRecordAddress(
            avatarAddress,
            raidId);
        var worldBossKillRewardRecordState = await StateGetter.GetWorldBossKillRewardRecordStateAsync(
            worldBossKillRewardRecordAddress,
            stoppingToken);
        await Store.UpsertStateDataManyAsync(
            CollectionNames.GetCollectionName<WorldBossKillRewardRecordDocument>(),
            [
                new WorldBossKillRewardRecordDocument(
                    worldBossKillRewardRecordAddress,
                    avatarAddress,
                    worldBossKillRewardRecordState)
            ],
            session,
            stoppingToken);
    }
}

using Bencodex.Types;
using Lib9c.Models.States;
using Libplanet.Crypto;
using Mimir.Worker.CollectionUpdaters;
using Mimir.Worker.Services;
using MongoDB.Driver;
using Nekoyume.Action;
using Nekoyume.Model.EnumType;
using Serilog;

namespace Mimir.Worker.ActionHandler;

public class BattleArenaHandler(IStateService stateService, MongoDbService store)
    : BaseActionHandler(
        stateService,
        store,
        "^battle_arena[0-9]*$",
        Log.ForContext<BattleArenaHandler>())
{
    private static readonly BattleArena Action = new();

    protected override async Task HandleAction(
        long blockIndex,
        Address signer,
        IValue actionPlainValue,
        string actionType,
        IValue? actionPlainValueInternal,
        IClientSessionHandle? session = null,
        CancellationToken stoppingToken = default)
    {
        Action.LoadPlainValue(actionPlainValue);
        await ItemSlotCollectionUpdater.UpdateAsync(
            StateService,
            Store,
            BattleType.Arena,
            Action.myAvatarAddress,
            Action.costumes,
            Action.equipments,
            null,
            stoppingToken);
        await ProcessArena(blockIndex, Action, session, stoppingToken);
    }

    private async Task ProcessArena(
        long blockIndex,
        BattleArena battleArena,
        IClientSessionHandle? session = null,
        CancellationToken stoppingToken = default)
    {
        var myArenaScore = await StateGetter.GetArenaScoreAsync(
            battleArena.myAvatarAddress,
            battleArena.championshipId,
            battleArena.round,
            stoppingToken);
        var myArenaInfo = await StateGetter.GetArenaInformationAsync(
            battleArena.myAvatarAddress,
            battleArena.championshipId,
            battleArena.round,
            stoppingToken);
        var myAvatarState = await StateGetter.GetAvatarState(
            battleArena.myAvatarAddress,
            stoppingToken);
        var mySimpleAvatarState = SimplifiedAvatarState.FromAvatarState(myAvatarState);
        await ArenaCollectionUpdater.UpsertAsync(
            Store,
            mySimpleAvatarState,
            myArenaScore,
            myArenaInfo,
            battleArena.myAvatarAddress,
            battleArena.championshipId,
            battleArena.round,
            session,
            stoppingToken);

        var enemyArenaScore = await StateGetter.GetArenaScoreAsync(
            battleArena.enemyAvatarAddress,
            battleArena.championshipId,
            battleArena.round,
            stoppingToken);
        var enemyArenaInfo = await StateGetter.GetArenaInformationAsync(
            battleArena.enemyAvatarAddress,
            battleArena.championshipId,
            battleArena.round,
            stoppingToken);
        var enemyAvatarState = await StateGetter.GetAvatarState(
            battleArena.enemyAvatarAddress,
            stoppingToken);
        var enemySimpleAvatarState = SimplifiedAvatarState.FromAvatarState(enemyAvatarState);
        await ArenaCollectionUpdater.UpsertAsync(
            Store,
            enemySimpleAvatarState,
            enemyArenaScore,
            enemyArenaInfo,
            battleArena.enemyAvatarAddress,
            battleArena.championshipId,
            battleArena.round,
            session,
            stoppingToken);
    }
}

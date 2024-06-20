using Bencodex;
using Bencodex.Types;
using HeadlessGQL;
using Libplanet.Action.Loader;
using Mimir.Worker.Handler;
using Mimir.Worker.Services;
using Nekoyume.Action.Loader;
using Serilog;

namespace Mimir.Worker.Poller;

public class BlockPoller : BaseBlockPoller
{
    private static readonly NCActionLoader ActionLoader = new();

    private readonly BaseActionHandler[] _handlers;

    private readonly IHeadlessGQLClient _headlessGqlClient;

    private readonly Codec _codec = new();

    public BlockPoller(
        IStateService stateService,
        IHeadlessGQLClient headlessGqlClient,
        MongoDbService store)
        : base(stateService, store, "BlockPoller", Log.ForContext<BlockPoller>())
    {
        _headlessGqlClient = headlessGqlClient;

        _handlers =
        [
            new BattleArenaHandler(stateService, store),
            new EventDungeonBattleHandler(stateService, store),
            new HackAndSlashHandler(stateService, store),
            new HackAndSlashSweepHandler(stateService, store),
            new JoinArenaHandler(stateService, store),
            new PatchTableHandler(stateService, store),
            new ProductsHandler(stateService, store),
            new RaidHandler(stateService, store),
            new RuneSlotStateHandler(stateService, store),
            new RaidActionHandler(stateService, store)
        ];
    }

    protected override async Task ProcessBlocksAsync(
        long syncedBlockIndex,
        long currentBlockIndex,
        CancellationToken stoppingToken)
    {
        long indexDifference = Math.Abs(currentBlockIndex - syncedBlockIndex);
        int limit = (int)(indexDifference > 100 ? 100 : indexDifference);

        _logger.Information(
            "Process block, current&sync: {CurrentBlockIndex}&{SyncedBlockIndex}, index-diff: {IndexDiff}, limit: {Limit}",
            currentBlockIndex,
            syncedBlockIndex,
            indexDifference,
            limit
        );

        await ProcessTransactions(syncedBlockIndex, limit, stoppingToken);
    }

    private async Task ProcessTransactions(
        long syncedBlockIndex,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var operationResult = await _headlessGqlClient.GetTransactions.ExecuteAsync(
            syncedBlockIndex,
            limit,
            cancellationToken
        );
        if (operationResult.Data is null)
        {
            var errors = operationResult.Errors.Select(e => e.Message);
            _logger.Error("Failed to get txs. response data is null. errors:\n{Errors}", errors);
            return;
        }

        var txs = operationResult.Data.Transaction.NcTransactions;
        if (txs is null || txs.Count == 0)
        {
            _logger.Information("Transactions is null or empty");
            return;
        }

        var actions = txs
            .Where(tx => tx is not null)
            .SelectMany(tx => tx!.Actions
                .Where(action => action is not null)
                .Select(action => action!.Raw)
                .ToList())
            .ToList();
        _logger.Information(
            "GetTransaction Success, tx-count: {TxCount}, action counts: {ActionsCounts}",
            txs.Count,
            actions.Count
        );
        var blockIndex = syncedBlockIndex + limit;
        foreach (var rawAction in actions)
        {
            var action = ActionLoader.LoadAction(
                blockIndex,
                _codec.Decode(Convert.FromHexString(rawAction)));
            var actionPlainValue = (Dictionary)action.PlainValue;
            var actionType = actionPlainValue.ContainsKey("type_id")
                ? (string)(Text)actionPlainValue["type_id"]
                : null;
            var actionPlainValueInternal = (Dictionary)actionPlainValue["values"];
            var handled = false;
            foreach (var handler in _handlers)
            {
                if (await handler.TryHandleAction(
                        blockIndex,
                        action,
                        actionType,
                        actionPlainValueInternal))
                {
                    handled = true;
                }
            }

            if (!handled)
            {
                _logger.Warning("Action is not handled. action: {Action}", actionPlainValue);
            }
        }

        await _store.UpdateLatestBlockIndex(syncedBlockIndex + limit, _pollerType);
    }
}

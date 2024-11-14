using Libplanet.Crypto;
using Mimir.MongoDB;
using Mimir.MongoDB.Bson;
using Mimir.Worker.Client;
using Mimir.Worker.Initializer.Manager;
using Mimir.Worker.Services;
using Mimir.Worker.StateDocumentConverter;
using Nekoyume;
using Nekoyume.TableData;
using Serilog;

namespace Mimir.Worker.Handler;

public sealed class ArenaParticipantHandler(
    MongoDbService dbService,
    IStateService stateService,
    IHeadlessGQLClient headlessGqlClient,
    IInitializerManager initializerManager)
    : BaseDiffHandler(
        CollectionName,
        Addresses.GetArenaParticipantAccountAddress(1, 1),
        new ArenaParticipantDocumentConverter(),
        dbService,
        stateService,
        headlessGqlClient,
        initializerManager,
        Log.ForContext<ArenaParticipantHandler>())
{
    private const string CollectionName = "arena_participant";
    
    private readonly MongoDbService _dbService = dbService;
    private readonly IStateService _stateService = stateService;
    private readonly IHeadlessGQLClient _headlessGqlClient = headlessGqlClient;

    private ArenaSheet? _arenaSheet;

    private ArenaSheet ArenaSheet
    {
        get
        {
            if (_arenaSheet is null)
            {
                _arenaSheet = _dbService.GetSheetAsync<ArenaSheet>().Result;
                if (_arenaSheet is null)
                {
                    throw new InvalidOperationException("ArenaSheet could not be retrieved from the database.");
                }
            }

            return _arenaSheet;
        }
    }

    protected override async Task<(long, long, long, long)> CalculateCurrentAndTargetIndexes(
        CancellationToken stoppingToken)
    {
        var syncedIndex = await GetSyncedBlockIndex(stoppingToken);
        var currentBaseIndex = syncedIndex;

        var accountAddress = GetAccountAddress(syncedIndex);
        Logger.Information(
            "{CollectionName} Synced BlockIndex: {SyncedBlockIndex}",
            CollectionName,
            syncedIndex);

        var currentIndexOnChain = await _stateService.GetLatestIndex(stoppingToken, accountAddress);
        var indexDifference = currentIndexOnChain - currentBaseIndex;
        var limit = CollectionName == CollectionNames.GetCollectionName<InventoryDocument>() ||
                    CollectionName == CollectionNames.GetCollectionName<AvatarDocument>()
            ? 1
            : 15;
        var currentTargetIndex = currentBaseIndex + (indexDifference > limit ? limit : indexDifference);
        if (currentBaseIndex >= currentTargetIndex)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
        }

        Logger.Information(
            "{CollectionName} Request diff data, current: {CurrentBlockIndex}, gap: {IndexDiff}, base: {CurrentBaseIndex} target: {CurrentTargetIndex}",
            CollectionName,
            currentIndexOnChain,
            indexDifference,
            currentBaseIndex,
            currentTargetIndex);

        return (currentBaseIndex, currentTargetIndex, currentIndexOnChain, indexDifference);
    }

    protected override async Task<long> GetSyncedBlockIndex(CancellationToken stoppingToken)
    {
        try
        {
            var syncedBlockIndex = await _dbService.GetLatestBlockIndexAsync(
                PollerType,
                CollectionName,
                stoppingToken);
            return syncedBlockIndex;
        }
        catch (InvalidOperationException)
        {
            var currentBlockIndex = await _stateService.GetLatestIndex(
                stoppingToken,
                accountAddress: null);
            Logger.Information(
                "Metadata collection is not found, set block index to {BlockIndex} - 1",
                currentBlockIndex);
            await _dbService.UpdateLatestBlockIndexAsync(
                new MetadataDocument
                {
                    PollerType = PollerType,
                    CollectionName = CollectionName,
                    LatestBlockIndex = currentBlockIndex - 1
                },
                cancellationToken: stoppingToken
            );
            return currentBlockIndex - 1;
        }
    }

    protected override async Task<DiffContext> ProduceByAccount(
        CancellationToken stoppingToken,
        long currentBaseIndex,
        long currentTargetIndex)
    {
        var accountAddress = GetAccountAddress(currentBaseIndex);
        var result = await _headlessGqlClient.GetAccountDiffsAsync(
            currentBaseIndex,
            currentTargetIndex,
            accountAddress,
            stoppingToken);

        return new DiffContext
        {
            AccountAddress = accountAddress,
            CollectionName = CollectionName,
            DiffResponse = result,
            TargetBlockIndex = currentTargetIndex
        };
    }

    private Address GetAccountAddress(long blockIndex)
    {
        var roundData = ArenaSheet.GetRoundByBlockIndex(blockIndex);
        return Addresses.GetArenaParticipantAccountAddress(roundData.ChampionshipId, roundData.Round);
    }
}

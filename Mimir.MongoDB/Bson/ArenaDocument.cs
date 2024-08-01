using Libplanet.Crypto;
using Nekoyume.Model.Arena;
using static Nekoyume.TableData.ArenaSheet;

namespace Mimir.MongoDB.Bson;

public record ArenaDocument(
    Address Address,
    ArenaInformation ArenaInformationObject,
    ArenaScore ArenaScoreObject,
    RoundData RoundData,
    Address AvatarAddress
) : IMimirBsonDocument(Address) { }

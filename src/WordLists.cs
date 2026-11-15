// Single clean words for the username finder. Rarer words first in spirit: common ones are almost all taken.

using System;
using System.Linq;

namespace CloveStrap
{
    static class WordLists
    {
        static string[] Split(string s)
        {
            return s.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Trim().ToLowerInvariant())
                    .Where(w => w.Length >= 3 && w.Length <= 20 && w.All(c => c >= 'a' && c <= 'z'))
                    .Distinct().ToArray();
        }

        public static readonly string[] English = Split(@"
abyss acorn adage aegis aerial affix agate agile alcove alder algae alibi allure almond alpine amber amble amity amulet anchor
anvil apex apricot arbor arcade arch ardent argent arid armada aroma arrow artisan ashen aspen astral astute atlas atoll attic
aura aurora austere autumn avalanche avid azure badger bairn ballad bamboo banner banyan barley baron basalt basil bastion
bayou beacon beckon bellow bevel bijou birch biscuit bistro blaze blithe bloom blossom blush bonfire boreal boulder bounty
bramble bravado breeze briar brimstone brisk brittle broach brook bubble buckle bugle bulwark burrow bygone cabin cactus
cadence cairn calico calm cameo candle canopy canter canyon caper capstone caramel caravan cardinal caribou carnival carol
cascade cashew castle catkin cavern cedar celery cellar cello chalice chalk chamber chamomile chapel charcoal chariot charm
chasm cherub chestnut chime chisel cider cinder cipher circuit citadel citrine clarion clover cobalt cobble cocoa comet
compass conch condor copper coral cordial corsair cosmos cottage cougar covert coyote cradle crag cranberry crater crescent
crest cricket crimson crocus crystal cumulus cupola curio cutlass cypress dahlia dainty damask dapper dazzle debonair decoy
delta denim derby dew dewdrop diadem dingo dirge divine docile dolphin dormouse dove dragonfly drizzle drowsy dulcet dune
dusk dynamo eagle earnest ebony echo eclipse eddy elder elfin elixir ember emblem embryo emerald enigma epoch equinox ermine
estuary ether evergreen falcon fathom fauna feather felicity fennel fern ferret fervor fiddle fiesta figment finch fjord
flannel flicker flint flora floret flourish fluent flurry foliage fondue forage forest fossil foxglove fragrant freckle
fresco frolic frost fudge fulcrum gable gadget gale gallant galleon garnet gazebo gecko gentle geyser ginger glacier glade
gleam glimmer glint glisten gloaming glow gnome goblet gondola gorge gossamer granite gravel griffin grotto grove gusto
halcyon hamlet harbor harmony harp harvest haven hazel heather hedgehog heirloom helix hemlock heron hickory hollow holly
honey horizon hornet hubbub humble hush hyacinth iceberg idyll igloo illume indigo inkwell inlet iris islet ivory jackal
jade jasmine jasper javelin jester jetty jewel jigsaw jingle jolly jonquil journey jovial jubilee juniper kayak kernel
kestrel kettle keystone kiln kindle kingfisher kinship kiwi knapsack knoll koala labyrinth lacquer lagoon lantern larch
lark lattice laurel lavender legend lemur lentil lichen lilac lily limber linen lintel locket lodestar loft lofty lotus
lullaby lumen luminous lupine luster lynx lyric macaw magenta magnet magnolia mallard mallow mammoth mango mantle maple
marble marigold mariner marmot marrow marsh marten marvel mascot meadow medley melody meridian merit mesa mica midnight
mimosa minnow minstrel mirage mirth mist mistral mittens moccasin mocha monarch monsoon moonbeam moor morsel mosaic moss
mossy mulberry muse musk mustang myrtle mystic nacre narwhal nebula nectar nettle nimble nomad nook northern nougat nova
nugget nutmeg oaken oasis oatmeal obsidian ocelot octave onyx opal orbit orchard orchid oriole osprey otter outpost owlet
oxbow oyster paddle pagoda palette panther papaya parable parasol parcel parchment parsley pastel pasture pebble pecan
pelican pendant penguin peony pepper perch peridot periwinkle petal pewter pheasant pilgrim pine pinnacle pinwheel piper
pistachio pixie plateau plume plover polar pollen pomelo poplar poppy porcelain portal posy prairie prism prologue puffin
pumice pumpkin quail quaint quarry quartz quasar quiet quill quince quiver radiant raffle rainfall rambler rampart raven
ravine realm reed reef relic remnant reverie riddle ridge ripple rivulet robin rococo roost rosemary rosewood rowan ruby
rudder rune russet rustic saffron saga sage sailor salmon salvo sapling sapphire sardine sash satchel satin savanna scarab
scarlet scepter scone scroll seafoam seashell sentinel sepia sequoia serene shale shamrock shimmer shingle shoal shore
sierra silhouette silk silver skylark slate sleet sloop smolder snowdrop solace solstice sonnet sorrel sparrow spindle
spire splendor sprig spruce squall starling stellar stork stratus summit sundial sunbeam swallow swan sycamore sylvan tabby
taffeta talisman tamarind tangerine tapestry tarragon tartan tawny teacup teal tempest tendril terrace thicket thimble
thistle thrush thyme tidal timber tinsel toffee topaz torrent toucan tranquil trellis trinket trout truffle tulip tumble
tundra turquoise twilight umber unicorn upland valiant valley vapor velvet verdant vermilion vesper vessel vigil vineyard
violet vireo vista vivid voyage walnut wander warbler wattle wavelet waxwing whimsy whisker whistle wicker wildfire willow
windmill wisp wisteria woodland wren yarrow yearling yonder zenith zephyr zinnia zodiac
abacus acumen adagio aether albatross alchemy almanac amaranth ambrosia anemone antler apogee aquifer arbiter archer
argonaut armature artifact asterism aviary azalea backwater balsam barnacle basilisk bauble beryl bilberry blackthorn
bluebell bobcat bolero boomerang botany brigand brocade buttercup caldera calypso camellia campfire caribou carousel
cassia catamaran celadon centaur chameleon chandelier chinook chrysalis cinnabar clementine cloudberry coachman cockatoo
columbine conifer constellation coriander cormorant cornflower cosmonaut cottonwood cranberry crossbow curlew cyclone
damselfly daybreak dervish dewberry dirigible dolmen downpour driftwood dusky edelweiss eiderdown elderberry emberglow
fairway farthing fiddlehead filbert firefly firelight flamingo foxfire freesia frostbite gardenia gemstone glowworm
goldfinch gooseberry gramophone grasshopper greenhouse guardian gumdrop harlequin hawthorn hearth hibiscus highland
hinterland hummingbird ironwood jackdaw jamboree jellyfish kaleidoscope kingdom knotweed lamplight landmark larkspur
lifeboat lighthouse limestone lionheart longbow mandolin manatee marzipan meadowlark merlin milkweed mistletoe moonstone
mountain nightjar nightshade nimbus northstar oakmoss ocarina oleander overture paintbrush palomino paperclip partridge
peppermint pinecone pipistrelle planetary porcupine primrose quicksilver raindrop rainstorm redwood riverbank rosebud
sandpiper sandstone seahorse seedling shipwright silverbell skyward snowfall songbird spearmint springtide starfish
stargazer starlight stonecrop sunflower sunstone swordfish tamarack teardrop thunderbird tidewater toadstool tumbleweed
undertow vagabond wanderer waterfall wavecrest weathervane wildflower windchime winterberry wolfsbane woodpecker
");

        public static readonly string[] Foreign = Split(@"
yami kage hoshi tsuki sora kaze yuki hana kumo mori kawa tori neko kitsune ryu tora yume kokoro hikari tenshi sakura
momiji kiri shiro kuro midori tama honoo mizu ishi hagane katana yumi ookami kujaku hotaru kaminari niji tsubasa sango
hisui kohaku ruri akatsuki yuugure shizuku kasumi arashi fubuki takara hibana senkou mikazuki
sombra luna cielo fuego tierra viento nube rayo trueno lobo zorro gato aguila cuervo lirio noche alba ocaso brisa niebla
hielo nieve llama chispa piedra plata hierro alma fiel bravo feroz mariposa estrella relampago girasol colibri tormenta
ombre lune soleil ciel terre vent nuage foudre loup renard aigle corbeau nuit aube brume glace neige flamme pierre argent
esprit papillon etoile orage tonnerre lumiere minuit velours
umbra nox ignis aqua terra ventus nubes fulmen lupus vulpes felis ursus aquila corvus stella astra caelum anima somnium
ferrum aurum argentum fortis celer ferox noctis aeternus aurora lux ventura
ombra luce fuoco acqua vento nuvola lupo volpe gatto orso corvo notte nebbia ghiaccio fiamma pietra argento ferro sangue
sogno veloce farfalla tempesta fulmine stella
schatten mond sonne himmel feuer wasser erde wolke blitz fuchs katze adler rabe nacht nebel schnee flamme stein silber
eisen seele traum schnell sturm donner funke
varjo kuu aurinko taivas tuli vesi tuuli pilvi salama susi kettu kissa karhu kotka korppi sumu lumi liekki kivi hopea
rauta sielu nopea myrsky revontuli tahti
byeol haneul baram gureum horangi yeou goyangi doksuri kkamagwi angae eoreum nabi bomnal
hoku mahina lani honua makani uila ilio pueo manu pulelehua pohaku uhane anuenue moana nalu
skuggi himinn eldr vatn hrafn ulfr refr bjorn nott snjor logi steinn silfr draumr
skia selene helios ouranos anemos nefos keravnos lykos alepou arkos aetos korakas omichli pagos chioni floga petra
argyros sidiros psyche oneiro tachys agrios asteri thalassa
");
    }
}

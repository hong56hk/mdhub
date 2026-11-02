using System.Globalization;
using System.Text;

namespace Mdhub.Services;

public sealed record EmojiEntry(string Value, string Name, string Category, string Keywords)
{
    internal string SearchText { get; } = EmojiCatalog.Normalize($"{Value} {Name} {Category} {Keywords}");
}

// A local, offline vocabulary: aliases describe intent as well as the visible object.
public static class EmojiCatalog
{
    public static IReadOnlyList<EmojiEntry> Entries { get; } = Parse();
    public static IReadOnlyList<EmojiEntry> Search(string? query, string? category = null)
    {
        var words = Normalize(query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Entries.Where(e => (category is null || e.Category == category)
            && words.All(word => e.SearchText.Contains(word, StringComparison.Ordinal))).ToList();
    }

    internal static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(char.ToLowerInvariant(c));
        return result.ToString();
    }

    private static IReadOnlyList<EmojiEntry> Parse()
    {
        var entries = new List<EmojiEntry>();
        var category = "";
        foreach (var line in Data.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#')) { category = line[1..]; continue; }
            var parts = line.Split('|');
            entries.Add(new(parts[0], parts[1], category, parts[2]));
        }
        return entries.AsReadOnly();
    }

    private const string Data = """
#Smileys
😀|Grinning face|happy smile cheerful joy pleased 开心 快乐 開心 高兴 高興 笑 嬉しい 행복
😃|Smiling face with open mouth|happy excited joyful laugh
😄|Smiling face with smiling eyes|happy smile joyful delighted
😁|Beaming face|happy grin smile teeth
😆|Laughing face|funny laugh hilarious happy
😅|Smiling face with sweat|relief nervous awkward embarrassed
😂|Tears of joy|laugh funny hilarious lol lmao laughing 哭笑 大笑
🤣|Rolling on the floor laughing|funny hilarious lol rofl laughing
🙂|Slightly smiling face|happy friendly smile okay
🙃|Upside down face|sarcasm ironic silly playful
😉|Winking face|wink joke flirt playful
😊|Blushing smiling face|happy shy kind blush grateful
😇|Smiling face with halo|angel innocent good blessed
🥰|Smiling face with hearts|love loved affection adore cute
😍|Heart eyes|love adore crush beautiful
🤩|Star struck|amazing wow impressed excited admiration
😘|Blowing a kiss|love affection kiss romantic
😋|Savoring food|delicious yummy tasty hungry
😛|Face with tongue|silly playful teasing joke
😜|Winking face with tongue|silly crazy playful joke
🤪|Zany face|crazy goofy silly fun
🤗|Hugging face|hug support care welcome embrace
🤭|Face with hand over mouth|oops giggle embarrassed surprise
🤫|Shushing face|quiet secret silence hush
🤔|Thinking face|think wondering question consider confused 思考 考える 생각
🫡|Saluting face|respect salute yes understood duty
🤐|Zipper mouth|secret quiet silent lips sealed
😐|Neutral face|blank indifferent speechless meh
😑|Expressionless face|unamused bored annoyed
😏|Smirking face|smug flirt confident
🙄|Rolling eyes|annoyed disbelief sarcastic bored
😬|Grimacing face|awkward nervous uncomfortable
😌|Relieved face|calm peace relaxed relief
😔|Pensive face|sad disappointed sorry unhappy 难过 難過 悲しい 슬픔
😢|Crying face|sad tear upset sorry unhappy 哭 悲しい 슬퍼
😭|Loudly crying face|sad sob crying devastated tears
😤|Face with steam from nose|frustrated angry determination huff
😡|Angry face|mad anger furious rage 生气 生氣 怒る 화남
🤬|Face with symbols on mouth|angry swearing furious cursing
😱|Screaming face|scared fear shock horror surprised
😨|Fearful face|scared afraid worried nervous
😰|Anxious face with sweat|worried anxious stress nervous
😥|Sad relieved face|disappointed sad relief worried
😓|Downcast face with sweat|stress tired worried difficult
🥺|Pleading face|please puppy eyes begging cute emotional
🥹|Face holding back tears|touched emotional grateful happy crying
😳|Flushed face|embarrassed surprised blush shocked
🥵|Hot face|heat hot sweating summer spicy
🥶|Cold face|cold frozen winter freezing
😴|Sleeping face|sleep tired bedtime sleepy rest
🥱|Yawning face|tired sleepy bored exhausted
🤤|Drooling face|hungry delicious desire sleepy
🤒|Face with thermometer|sick ill fever unwell
🤕|Face with bandage|hurt injury sick accident
🤢|Nauseated face|sick disgust nausea gross
🤮|Vomiting face|sick vomit disgust gross
🤧|Sneezing face|sick cold allergy sneeze
😷|Face with medical mask|sick health protection doctor
🤯|Exploding head|mind blown shocked amazed overwhelmed
🥳|Partying face|celebrate celebration happy birthday party congratulations
😎|Smiling face with sunglasses|cool confident stylish summer
🤓|Nerd face|smart geek study learning nerdy
🧐|Face with monocle|curious inspect investigate skeptical
🤠|Cowboy face|cowboy western yeehaw adventure
🤡|Clown face|clown circus silly joke foolish
👻|Ghost|halloween spooky scary boo
💀|Skull|dead death danger halloween dying laughing
🤖|Robot|robot technology ai automation computer
💩|Pile of poo|poop mess silly bad rubbish
#People
👋|Waving hand|hello goodbye hi wave greeting welcome 你好 再见 再見
🤚|Raised back of hand|stop high five hand
✋|Raised hand|stop hand high five volunteer
👌|OK hand|okay perfect good agreement
✌️|Victory hand|peace victory two success
🤞|Crossed fingers|good luck hope wish hopeful
🤟|Love you gesture|love affection hand
🤘|Sign of the horns|rock music metal concert
🤙|Call me hand|phone call hang loose
👍|Thumbs up|yes good approve like agree well done 赞 讚 同意
👍🏽|Thumbs up medium skin tone|yes good approve like agree well done
👎|Thumbs down|no bad dislike disagree reject
✊|Raised fist|solidarity strength power support
👊|Oncoming fist|fist bump punch strength
👏|Clapping hands|applause congratulations bravo well done celebrate
🙌|Raising hands|hooray celebrate praise success yay
👐|Open hands|welcome openness hug
🤝|Handshake|agreement deal partnership teamwork meeting
🙏|Folded hands|thanks thank you grateful gratitude pray please hope namaste 谢谢 謝謝 感谢 感謝 ありがとう 감사
💪|Flexed biceps|strong strength workout gym effort motivation
🫶|Heart hands|love care support affection
👀|Eyes|look see watching attention curious
🧠|Brain|think smart intelligence learning mind
👶|Baby|child newborn infant family
🧒|Child|kid young family
🧑|Person|human adult people
👩|Woman|female person people
👨|Man|male person people
👩‍💻|Woman technologist|developer programmer computer coding work software engineer
👨‍💻|Man technologist|developer programmer computer coding work software engineer
🧑‍💻|Technologist|developer programmer computer coding work software engineer
🧑‍⚕️|Health worker|doctor nurse medical health hospital
🧑‍🏫|Teacher|teach school learning education
🧑‍🍳|Cook|chef cooking food kitchen
🧑‍🚀|Astronaut|space astronaut exploration
👑|Crown|king queen royal winner leader
#Nature
🐱|Cat face|cat kitten pet meow animal 猫 貓 ねこ 고양이
🐶|Dog face|dog puppy pet woof animal 狗 いぬ 강아지
🐭|Mouse face|mouse rodent animal
🐰|Rabbit face|rabbit bunny animal easter
🦊|Fox|fox animal woodland
🐻|Bear|bear animal teddy
🐼|Panda|panda bear animal bamboo
🐨|Koala|koala australia animal
🐯|Tiger face|tiger wild cat animal
🦁|Lion|lion wild cat animal brave
🐮|Cow face|cow farm milk animal
🐷|Pig face|pig farm animal
🐸|Frog|frog amphibian green animal
🐵|Monkey face|monkey animal silly
🙈|See no evil monkey|shy embarrassed hide peek monkey
🐧|Penguin|penguin bird cold animal
🐦|Bird|bird fly nature animal
🦋|Butterfly|butterfly insect beauty transformation
🐝|Honeybee|bee honey insect busy
🐢|Turtle|turtle tortoise slow animal
🐬|Dolphin|dolphin ocean sea animal
🐳|Whale|whale ocean sea animal
🌸|Cherry blossom|flower spring pink blossom beautiful
🌹|Rose|flower love romance red
🌻|Sunflower|flower summer sunny yellow
🌷|Tulip|flower spring garden
🌱|Seedling|plant growth new beginning sprout
🌳|Deciduous tree|tree forest nature green
🌲|Evergreen tree|tree pine forest nature
🍀|Four leaf clover|good luck lucky irish shamrock
🍁|Maple leaf|autumn fall canada leaf
🌍|Globe Europe Africa|world earth planet global environment
🌈|Rainbow|rainbow color hope pride weather
☀️|Sun|sunny sunshine bright hot weather summer
🌤️|Sun behind small cloud|weather partly cloudy sunshine
☁️|Cloud|cloudy sky weather
🌧️|Cloud with rain|rain rainy wet weather
⛈️|Thunderstorm|storm lightning thunder rain weather
❄️|Snowflake|snow cold winter ice weather
🌙|Crescent moon|moon night sleep evening
⭐|Star|star favorite favourite rating special night
✨|Sparkles|magic shiny clean special sparkle excitement
🔥|Fire|hot fire flame amazing trending lit
💧|Droplet|water drop rain liquid
#Food
🍎|Red apple|apple fruit healthy food
🍊|Tangerine|orange citrus fruit food
🍋|Lemon|lemon citrus sour fruit food
🍌|Banana|banana fruit food
🍉|Watermelon|watermelon summer fruit food
🍇|Grapes|grapes fruit wine food
🍓|Strawberry|strawberry berry fruit sweet food
🫐|Blueberries|blueberry berry fruit food
🍒|Cherries|cherry fruit food
🍑|Peach|peach fruit food
🥑|Avocado|avocado vegetable healthy food
🥦|Broccoli|broccoli vegetable healthy green food
🥕|Carrot|carrot vegetable healthy food
🌽|Corn|corn vegetable food
🍞|Bread|bread toast bakery breakfast food
🥐|Croissant|croissant pastry bakery breakfast food
🧀|Cheese|cheese dairy food
🍔|Hamburger|burger fast food lunch dinner
🍟|French fries|fries chips fast food potato
🍕|Pizza|pizza italian food dinner party
🌮|Taco|taco mexican food dinner
🍜|Steaming bowl|ramen noodles soup food dinner
🍣|Sushi|sushi japanese fish food dinner
🍚|Cooked rice|rice food dinner asian
🥗|Green salad|salad healthy vegetable food
🍿|Popcorn|popcorn movie cinema snack
🍩|Doughnut|donut doughnut sweet dessert snack
🍪|Cookie|cookie biscuit sweet snack dessert
🎂|Birthday cake|happy birthday celebration party cake dessert
🍰|Shortcake|cake dessert sweet treat
🍫|Chocolate bar|chocolate sweet candy snack
🍦|Ice cream|icecream ice cream dessert cold summer
☕|Hot beverage|coffee cafe café tea drink morning caffeine break
🍵|Tea without handle|tea green matcha drink calm
🧋|Bubble tea|boba milk tea drink tapioca
🥤|Cup with straw|soda juice drink soft beverage
🍺|Beer mug|beer drink pub cheers alcohol
🥂|Clinking glasses|cheers champagne toast celebration congratulations drink
🍷|Wine glass|wine drink dinner alcohol
#Travel
🚗|Car|car automobile drive road travel commute
🚕|Taxi|taxi cab transport ride travel
🚌|Bus|bus public transport commute travel
🚲|Bicycle|bike cycle cycling ride travel exercise
🏍️|Motorcycle|motorcycle motorbike ride road travel
🚆|Train|train rail railway commute travel
🚇|Metro|subway metro underground tube transport travel
✈️|Airplane|plane flight fly airport vacation holiday travel
🚀|Rocket|rocket launch startup space fast ship success
🚁|Helicopter|helicopter fly transport
🚢|Ship|ship boat sea cruise travel
⛵|Sailboat|boat sailing sea ocean travel
🛶|Canoe|canoe kayak river boat adventure
🏠|House|home house building family
🏢|Office building|office work business building
🏥|Hospital|hospital doctor health medical building
🏫|School|school education learning building
🏰|Castle|castle fairy tale building holiday
🏕️|Camping|camping tent outdoors adventure vacation
🏖️|Beach with umbrella|beach sea sun vacation holiday relax
🏔️|Snow capped mountain|mountain hiking snow adventure outdoors
🗺️|World map|map navigation explore travel location
📍|Location pin|location place map address here
🧳|Luggage|suitcase baggage travel vacation holiday packing
⛱️|Umbrella on ground|beach holiday vacation relax
#Activities
⚽|Soccer ball|soccer football sport game team
🏀|Basketball|basketball sport game team
🏈|American football|football sport game team
⚾|Baseball|baseball sport game team
🎾|Tennis|tennis sport racket game
🏐|Volleyball|volleyball sport game team
🏓|Ping pong|table tennis ping pong sport game
🏸|Badminton|badminton sport racket game
🏊|Swimmer|swim swimming pool sport exercise
🏃|Runner|running jogging marathon sport exercise
🧘|Person in lotus position|yoga meditate meditation calm relax exercise
🏋️|Weight lifter|gym fitness lifting strong exercise workout
🏆|Trophy|winner win victory award champion success congratulations
🥇|Gold medal|first winner win gold achievement congratulations
🎯|Bullseye|target goal aim focus success
🎮|Video game|game gaming controller play fun
🎲|Game die|dice board game chance luck play
🧩|Puzzle piece|puzzle solve problem game autism
🎨|Artist palette|art painting draw design creative color
🎭|Performing arts|theater theatre drama masks acting
🎬|Clapper board|movie film cinema video production
🎵|Musical note|music song singing melody
🎶|Musical notes|music song melody dancing
🎤|Microphone|sing karaoke microphone speech music
🎸|Guitar|guitar music rock instrument
🎹|Musical keyboard|piano keyboard music instrument
🎉|Party popper|celebrate celebration congratulations happy birthday party hooray success
🎊|Confetti ball|celebrate celebration congratulations party success
🎁|Wrapped gift|gift present birthday surprise celebration
🎈|Balloon|birthday party celebration balloon
🎄|Christmas tree|christmas holiday festive december
🎃|Jack o lantern|halloween pumpkin spooky october
#Objects
💡|Light bulb|idea insight inspiration creative solution innovation
📌|Pushpin|pin pinned reminder important notice
📝|Memo|note write writing memo document todo
📚|Books|book reading study school education learn library
📖|Open book|book reading learn story education
📓|Notebook|notebook journal diary notes writing
📅|Calendar|date schedule appointment plan meeting reminder
⏰|Alarm clock|alarm time wake reminder deadline
⌛|Hourglass|time wait waiting countdown deadline
💻|Laptop|computer laptop work coding developer technology office
⌨️|Keyboard|keyboard typing computer technology
🖥️|Desktop computer|computer monitor desktop work technology
📱|Mobile phone|phone mobile smartphone call message technology
📷|Camera|camera photo photography picture
🎧|Headphones|headphones music listening sound audio
🔋|Battery|battery energy charge power
🔌|Electric plug|plug power charge electricity
🔍|Magnifying glass|search find look investigate zoom
🔑|Key|key unlock access password security
🔒|Locked|lock private secure password security
🔓|Unlocked|unlock open access security
🛠️|Hammer and wrench|tools fix repair build maintenance settings
⚙️|Gear|settings gear configuration tools engineering
💼|Briefcase|work office business job career
💰|Money bag|money cash rich payment finance salary
💳|Credit card|card payment money shopping bank
📦|Package|box delivery parcel shipping order
🛒|Shopping cart|shopping cart groceries buy store
🗑️|Wastebasket|trash rubbish delete remove bin clean
✏️|Pencil|pencil edit write draw writing
🖊️|Pen|pen writing edit sign
✉️|Envelope|email mail letter message communication
📬|Mailbox|mail inbox letter communication
#Symbols
❤️|Red heart|love affection romance care heart 爱 愛 喜欢 喜歡 사랑
🧡|Orange heart|love affection friendship heart
💛|Yellow heart|love friendship happy heart
💚|Green heart|love nature heart envy
💙|Blue heart|love trust calm heart
💜|Purple heart|love affection heart
🖤|Black heart|love grief dark heart
🤍|White heart|love peace pure heart
🤎|Brown heart|love warmth heart
💔|Broken heart|heartbreak sad breakup lost love
💕|Two hearts|love romance affection hearts
💖|Sparkling heart|love affection special heart
💯|Hundred points|perfect full marks excellent success hundred
✅|Check mark button|yes done complete task success correct approve
✔️|Check mark|yes correct done tick complete
❌|Cross mark|no wrong cancel error failed reject
❎|Cross mark button|no wrong cancel error reject
⚠️|Warning|warning caution danger attention alert
🚫|Prohibited|no forbidden stop banned not allowed
⛔|No entry|stop forbidden no entry restricted
❓|Question mark|question help confused ask why
❗|Exclamation mark|important attention alert urgent notice
‼️|Double exclamation mark|important urgent attention surprise
💬|Speech balloon|chat talk comment message conversation
💭|Thought balloon|think thought dream idea
💤|Zzz|sleep sleepy rest snore
♻️|Recycling symbol|recycle sustainable reuse environment green
☮️|Peace symbol|peace calm harmony
☯️|Yin yang|balance harmony yin yang
➕|Plus|add plus positive more
➖|Minus|minus remove subtract less
➡️|Right arrow|right forward next direction
⬅️|Left arrow|left back previous direction
⬆️|Up arrow|up increase direction
⬇️|Down arrow|down decrease direction
🔄|Counterclockwise arrows|refresh reload repeat sync update
🔗|Link|link url connect connection chain
#Flags
🏳️|White flag|flag surrender peace truce
🏴|Black flag|flag black pirate
🏁|Chequered flag|flag finish race complete winner
🚩|Triangular flag|flag warning red flag danger marker
🏳️‍🌈|Rainbow flag|pride rainbow lgbt lgbtq equality flag
🏳️‍⚧️|Transgender flag|trans transgender pride equality flag
🇬🇧|United Kingdom flag|uk british britain england london flag
🇺🇸|United States flag|us usa american america flag
🇨🇦|Canada flag|canadian canada maple flag
🇦🇺|Australia flag|australian australia flag
🇳🇿|New Zealand flag|new zealand kiwi flag
🇫🇷|France flag|france french paris flag
🇩🇪|Germany flag|germany german berlin flag
🇪🇸|Spain flag|spain spanish flag
🇮🇹|Italy flag|italy italian rome flag
🇯🇵|Japan flag|japan japanese tokyo flag 日本
🇰🇷|South Korea flag|korea korean seoul flag 한국
🇨🇳|China flag|china chinese beijing flag 中国 中國
🇭🇰|Hong Kong flag|hong kong flag 香港
🇹🇼|Taiwan flag|taiwan flag 台湾 台灣
🇮🇳|India flag|india indian flag
🇧🇷|Brazil flag|brazil brazilian flag
🇲🇽|Mexico flag|mexico mexican flag
🇺🇦|Ukraine flag|ukraine ukrainian flag
🇪🇺|European Union flag|europe european union eu flag
🇺🇳|United Nations flag|united nations un international flag
""";
}

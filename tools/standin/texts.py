"""Original message text for the stand-in data set, written for OpenCivOne (not the original game's wording).

The game looks up each message by its *KEY. Placeholders the game fills in: $US, $THEM, $BUCKS, $RPLC1 and $RPLC2
(which of them a message gets depends on where it is shown). A line starting with _ is a choice in a dialog; where
the game acts on the answer, choice 1 (the second) is the one that accepts. A ^ splits the end-of-game texts into
the lines they are shown one at a time.
"""

MESSAGES = {
    # ----- the start of the game
    '*INIT': ('$RPLC1, the gods have chosen you to lead the $US. Your people are few, wandering the land in search of '
              'a home. Found a city, gather knowledge, and guide them through the ages, so that the $US may build an '
              'empire to outlast time itself.'),
    '*FIRSTMOVE': ('Our settlers await your orders. Move them with the arrow keys or the numeric keypad, and press B '
                   'to build a city where the land is good: grassland, plains and rivers feed a city best.'),
    '*FIRSTCIV': ('Our scholars have made their first discovery! Each new advance opens new units, buildings and '
                  'ideas. Choose what they study next with care.'),
    '*FIRSTPRODUCT': ('Our city has finished its first work. Open the city (click on it) to choose what it builds '
                      'next: settlers to found more cities, warriors to guard it, or buildings to help it grow.'),
    '*FIRSTUNIT1': ('Our first unit is ready. Each unit moves in turn; when all have moved, the turn ends. Press the '
                    'space bar to skip a unit, or F to fortify it in place.'),
    '*FIRSTUNIT2': ('A unit in a city defends it. Fortified units defend better still, and a city without defenders '
                    'is easy prey.'),
    '*HELP1': ('Advisor\'s note: more cities mean more food, trade and production. Keep settlers at work founding '
               'new cities while land is free.'),
    '*HELP2': ('Advisor\'s note: roads speed travel and raise trade; irrigation feeds growing cities; mines give '
               'shields on hills. Put your settlers to work on the land around your cities.'),
    '*MONARCHY': ('Our scholars now understand Monarchy. A king rules more firmly than a tribal chief: change '
                  'government (from the Game menu, Revolution) to gain more food, shields and trade.'),

    # ----- instant advice when a settler or unit acts
    '*BUILDCITY': 'A good site for a city: open land with food nearby. Press B to build a city here.',
    '*IRRIGATE': 'Settlers can irrigate this land beside water (press I) to grow more food for $US.',
    '*MINING': 'Settlers can dig a mine in these hills (press M) to give more shields to $US.',
    '*ROAD': 'Settlers can build a road here (press R): roads speed movement and add trade for $US.',
    '*SETTLERS': 'Only settlers can do that.',
    '*ADDCITY': 'Settlers can join a city to add to its population, or found a new city elsewhere.',
    '*NOIRR': 'Nothing on this land can be irrigated.',
    '*NOWATER': 'Irrigation needs water: an ocean, a lake, a river or irrigated land beside this square.',
    '*NOMINE': 'Mines can only be dug in hills, mountains or desert.',
    '*PILLAGE': 'Pillaging destroys the improvements on this square.',
    '*ZOC': ('Our unit may not move from one square beside an enemy unit to another beside it. Go round, or attack '
             'first.'),
    '*AMPHIB': 'Land units cannot attack from a ship. Land first, then attack.',
    '*FIGHTER': 'Only fighters can attack aircraft in flight.',
    '*FUEL': 'Aircraft must end their flight in a city or on a carrier, or they run out of fuel and are lost.',
    '*OCCUPY': 'Only land units can capture a city.',
    '*TRIREME': 'The trireme is not built for the open sea and may be lost if it ends its move away from the coast.',
    '*UMAX': 'Defense Minister: Our empire cannot keep any more units in the field.',
    '*PMAX': 'Domestic Advisor: No more cities can be founded; our empire has reached its limit.',
    '*TAXES': ('Domestic Advisor: Our treasury grows thin. Raise taxes (from the Game menu, Tax Rate) or sell a '
               'building, before our buildings must be sold to pay for their upkeep.'),
    '*LEADER': 'Our capital has fallen and our leader has been captured! Our people are in confusion.',
    '*REV': ('The people demand a change of government. Shall we hold a revolution? It brings a few turns of '
             'anarchy.\n_Not now\n_Revolution!'),
    '*PALACE': ('Your people honour your rule! In gratitude they will add to your palace. Choose which part of it '
                'they build.'),

    # ----- disasters in a city (the city is shown on the map)
    '*D1': 'Plague! Disease spreads through the crowded streets. Citizens demand an AQUEDUCT.',
    '*D2': 'Flood! The river has burst its banks and washed away homes. Citizens demand CITY WALLS.',
    '*D3': 'Volcano! Fire and ash fall on the city. The people beg for a TEMPLE to calm the mountain.',
    '*D4': 'Famine! The harvest has failed and the food stores are empty.',
    '*D5': 'Earthquake! The ground shook and the $US was destroyed.',
    '*D6': 'Pirates! Raiders from the sea have looted the city\'s stores. Citizens demand BARRACKS.',

    # ----- diplomacy: the other leader speaks
    '*ATTITUDE': 'We do not trust you. Our patience with you is not without limit.',
    '*ATWAR': 'We have nothing to say to you. Our armies will speak for us. Our war with the $RPLC1 goes on.',
    '*NOCONTACT': 'We know nothing of the $RPLC1. We have had no dealings with them.',
    '*PEACE': 'Our peoples are weary of war. Shall we make peace?\n_No, the war goes on\n_Yes, let us make peace',
    '*TREATY': ('So it is agreed. Let there be peace between the $US and the $RPLC1, and may it last for '
                'generations.'),
    '*EXCHANGE': ('Our scholars would learn from yours. We will teach you $RPLC1 in return for knowledge of your '
                  'own.\n_No, we keep our secrets\n_Agreed, an exchange'),
    '*TAKECIV': ('You are weak, and we are strong. Give us the secret of $RPLC1, or face our armies.\n_Never!\n_Very '
                 'well, take it'),
    '*TRIBUTE': ('Your lands would be safer with our friendship. Pay us $BUCKS gold as tribute, and we will leave '
                 'you in peace.\n_We will not pay\n_We will pay'),
    '*CANCEL': ('We $RPLC1 that you break your treaty with the $THEM. Their friends are no friends of ours.\n_We '
                'refuse\n_We will break it'),
    '*GIVECIV': ('As a sign of our good will, we offer you the secret of $RPLC1.\n_We have no need of it\n_We accept '
                 'your gift'),
    '*GIVECASH': ('Take this gift of $BUCKS gold, and let there be friendship between us.\n_Keep your gold\n_We '
                  'accept'),
    '*GIVEMORE': 'Very well. Take $BUCKS gold more, and go in peace.',
    '*GROVEL': ('Spare us, we beg you! Take all our treasure, $BUCKS gold, and let us live in peace.\n_No mercy\n_We '
                'accept your gold'),
    '*MERCENARY': ('For $BUCKS gold, our armies would march against your enemies.\n_We decline\n_We will pay'),
    '*UNFORTUNATE': 'Most unfortunate. You cannot pay the price, so there is no bargain.',
    '*THREAT': ('Your armies come too close to our borders. What do you want with the $RPLC1?\n_We come in '
                'peace\n_Join us against our enemies\n_Pay tribute, or be destroyed'),
    '*MOBILIZE': 'Then it is war! Our armies are on the march.',
    '*PROVOKE': 'You dare to insult us? You will regret it!',
    '*REJECT': 'We reject your offer.',
    '*RID': 'Leave our sight, and do not return.',
    '*HELPME': ('The $THEM are attacking us! Help us with $BUCKS gold and we will not forget it.\n_We cannot '
                'help\n_We will help'),
    '*CRUSADE': ('The $THEM threaten us both. Will you $RPLC1 alliance against them?\n_No\n_Yes'),
    '*GAPE': 'The $US have discovered $RPLC1! Our scholars are amazed.',
    '*SCHISM': ('Civil war! Our empire has split in two. The rebels name themselves the $THEM, and break away from '
                'the $US.'),
    '*ESCHISM': 'Civil war among the $US! Rebels have broken away and name themselves the $THEM.',

    # ----- the end of the game
    '*FAME': ('Your reign has come to its end. Will you see your place among the great rulers of '
              'history?\n_No, finish here\n_Yes, show me'),
    '*ARCH': ('Thousands of years later...^Archaeologists dig among the ruins of a forgotten people.^They find '
              'roads, walls and the bones of great cities.^The last of their knowledge was $RPLC1.^Of their rulers, '
              'nothing is remembered.'),
    '*SPACE': ('In $RPLC2, the spaceship of the $THEM reaches Alpha Centauri.^$BUCKS settlers step out under a new '
               'sun.^The people of Earth will remember the name of $US.^A new age begins among the stars.'),
    '*DISORDER': 'Civil disorder! The citizens riot in the streets and all work in the city stops.',

    # ----- choosing the world at the start of a game
    '*LAND': 'How much land shall the world have?\n_Small\n_Normal\n_Large',
    '*TEMPERATURE': 'How warm shall the world be?\n_Cool\n_Temperate\n_Warm',
    '*CLIMATE': 'How wet shall the world be?\n_Arid\n_Normal\n_Wet',
    '*AGE': 'How old shall the world be?\n_3 billion years\n_4 billion years\n_5 billion years',

    '*SPACE2': ('In $RPLC2, the spaceship of the $THEM reaches Alpha Centauri.^$BUCKS settlers begin a new world, '
                'and the stars belong to the $THEM.^Your people watch from Earth.'),
}

# random news items for the newspaper (*NEWSA to *NEWSN)
NEWS = [
    'Harvest festival draws record crowds.',
    'Scholars debate the shape of the world.',
    'New road brings merchants from afar.',
    'Weather fair; farmers expect a good year.',
    'Local musicians perform for the court.',
    'Price of bread steady this season.',
    'Fishermen report a large catch.',
    'Builders complain of a shortage of stone.',
    'Sports: crowds cheer the chariot races.',
    'Travelers tell of strange lands beyond the sea.',
    'Physicians warn of a cold winter.',
    'Citizens gather to hear the town crier.',
    'Market day brings goods from every province.',
    'Astronomers sight a bright new star.',
]

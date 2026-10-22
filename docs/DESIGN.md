# War: design

The second Renegade card game, on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)).

## The game
War for two, you against Rosie (the Bicycle rules). The deck is dealt out face down, 26 each.
Each battle, both turn up their top card; the higher rank (aces high) takes both to the bottom
of their pile. Equal ranks mean war: each puts one card face down and turns up another, and the
higher takes everything (a war can lead to another). A player who can't turn a card up loses.
Rules in `src/Core/WarGame.cs` (tested).

## Feel
- The deck is shuffled in front of you (riffles and a bridge) and dealt out briskly.
- Glass hands flick each card onto the felt with a pinch; the winner's hand sweeps the battle
  under its pile. "War!" shows over Rosie's side.
- Signature effect: the winner's pile catches fire.

## Settings
- **Game length:** every card (classic) or 26 battles (most cards wins).
- **Flip automatically:** plays the battles for you.

## Scope for version 1
Two players, the full table, tutorial, Send, ads and the shop. War has no choices to make, so
there are no computer levels. Later: tutorial clips, three or four players.

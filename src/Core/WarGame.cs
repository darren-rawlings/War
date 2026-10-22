using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace War.Core;

/// <summary>Something that happened, in order, for the table to act out.</summary>
public abstract record WarEvent;

/// <summary>A player turned their top card face up into the battle.</summary>
public sealed record Turned(int Player, Card Card) : WarEvent;

/// <summary>A tie for the highest card: war! Those players put cards face down, then turn another.</summary>
public sealed record WarDeclared(int Depth, IReadOnlyList<int> Players) : WarEvent;

/// <summary>A player put their top card face down onto the war.</summary>
public sealed record FaceDown(int Player, Card Card) : WarEvent;

/// <summary>
/// The battle's winner takes every card in it (in order) to the bottom of their pile;
/// <see cref="Hidden"/> are the ones that went down face down in its wars.
/// </summary>
public sealed record Won(int Player, IReadOnlyList<Card> Cards, IReadOnlyList<Card> Hidden) : WarEvent;

/// <summary>A player has run out of cards and leaves the game.</summary>
public sealed record Out(int Player) : WarEvent;

/// <summary>The game is over: one player has every card, or (quick game) the battles ran out.</summary>
public sealed record GameOver(int Winner, int[] Cards) : WarEvent;

/// <summary>
/// War for two to four players (the Bicycle rules). The deck is dealt out face down, one at a
/// time round the table. Each battle, everyone turns up their top card; the highest rank (aces
/// high) takes all of them to the bottom of their pile. When two or more tie for the highest, those
/// players go to war: each puts cards face down (three, or one in the gentler version), keeping a
/// card back to turn up if they're short, then turns up another, and the highest takes everything
/// on the table (wars can repeat). A player who can't turn a card up loses the war; a player with
/// no cards left is out. The classic game is won by taking every card; a quick game ends after a
/// set number of battles, the most cards winning (or a tie).
/// </summary>
public sealed class WarGame
{
	private readonly Queue<Card>[] _piles;
	private readonly List<Card> _onTable = new();
	private readonly bool[] _out;

	public WarGame(IReadOnlyList<Card> deck, int battleLimit = 0, int players = 2, int faceDown = 3)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("War needs a full deck.", nameof(deck));
		if (players is < 2 or > 4)
			throw new ArgumentOutOfRangeException(nameof(players));
		Players = players;
		FaceDownCount = Math.Max(1, faceDown);
		_piles = Enumerable.Range(0, players).Select(_ => new Queue<Card>()).ToArray();
		_out = new bool[players];
		Dealt = deck.ToList();
		for (int i = 0; i < deck.Count; i++)
			_piles[i % players].Enqueue(deck[i]);
		BattleLimit = battleLimit;
	}

	public WarGame(Random random, int battleLimit = 0, int players = 2, int faceDown = 3) : this(Deck.Shuffled(random), battleLimit, players, faceDown) { }

	public int Players { get; }

	/// <summary>How many cards each player puts face down in a war (three, or one).</summary>
	public int FaceDownCount { get; }

	/// <summary>The cards dealt, top first, one at a time round the table from player 0.</summary>
	public IReadOnlyList<Card> Dealt { get; }

	/// <summary>0 for the classic game (to every card); otherwise how many battles a quick game lasts.</summary>
	public int BattleLimit { get; }

	public int Battles { get; private set; }
	public int Count(int player) => _piles[player].Count;
	public bool IsOut(int player) => _out[player];

	/// <summary>A player's pile, top card first.</summary>
	public IReadOnlyList<Card> Pile(int player) => _piles[player].ToList();

	public bool IsOver { get; private set; }

	/// <summary>The winner once the game is over, or -1 for a tie (or before).</summary>
	public int Winner { get; private set; } = -1;

	/// <summary>Ace counts high in War.</summary>
	public static int Value(Card card) => card.Rank == Card.Ace ? 14 : card.Rank;

	/// <summary>Fights one battle (with any wars it leads to) and reports each step.</summary>
	public List<WarEvent> Battle()
	{
		if (IsOver)
			throw new InvalidOperationException("The game is over.");
		var events = new List<WarEvent>();
		_onTable.Clear();
		var hidden = new List<Card>();
		Battles++;
		int depth = 0;
		var contenders = Enumerable.Range(0, Players).Where(p => !_out[p]).ToList();
		int winner;
		while (true)
		{
			// Each contender turns up their top card; one with none left loses this battle.
			var up = new Dictionary<int, Card>();
			foreach (int p in contenders)
				if (_piles[p].Count > 0)
				{
					up[p] = _piles[p].Dequeue();
					_onTable.Add(up[p]);
					events.Add(new Turned(p, up[p]));
				}
			var still = contenders.Where(up.ContainsKey).ToList();
			if (still.Count == 0)
			{
				// Everyone in the war ran out at once: the table goes to whoever has most cards left.
				winner = Enumerable.Range(0, Players).OrderByDescending(Count).First();
				break;
			}
			int best = still.Max(p => Value(up[p]));
			var top = still.Where(p => Value(up[p]) == best).ToList();
			if (top.Count == 1)
			{
				winner = top[0];
				break;
			}
			// War among those tied: cards face down (keeping one back to turn up), then go again.
			depth++;
			events.Add(new WarDeclared(depth, top));
			foreach (int p in top)
				for (int k = 0; k < FaceDownCount && _piles[p].Count > 1; k++)
				{
					var down = _piles[p].Dequeue();
					_onTable.Add(down);
					hidden.Add(down);
					events.Add(new FaceDown(p, down));
				}
			contenders = top;
		}
		foreach (var card in _onTable)
			_piles[winner].Enqueue(card);
		events.Add(new Won(winner, _onTable.ToList(), hidden));
		_onTable.Clear();

		for (int p = 0; p < Players; p++)
			if (!_out[p] && _piles[p].Count == 0)
			{
				_out[p] = true;
				events.Add(new Out(p));
			}
		var left = Enumerable.Range(0, Players).Where(p => !_out[p]).ToList();
		if (left.Count == 1)
			End(events, left[0]);
		else if (BattleLimit > 0 && Battles >= BattleLimit)
		{
			int most = left.Max(Count);
			var leaders = left.Where(p => Count(p) == most).ToList();
			End(events, leaders.Count == 1 ? leaders[0] : -1);
		}
		return events;
	}

	private void End(List<WarEvent> events, int winner)
	{
		IsOver = true;
		Winner = winner;
		events.Add(new GameOver(winner, Enumerable.Range(0, Players).Select(Count).ToArray()));
	}
}

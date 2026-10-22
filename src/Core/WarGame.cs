using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace War.Core;

/// <summary>Something that happened, in order, for the table to act out.</summary>
public abstract record WarEvent;

/// <summary>A player turned their top card face up into the battle.</summary>
public sealed record Turned(int Player, Card Card) : WarEvent;

/// <summary>Equal ranks: war! Each player puts a card face down (if they can), then turns another.</summary>
public sealed record WarDeclared(int Depth) : WarEvent;

/// <summary>A player put their top card face down onto the war.</summary>
public sealed record FaceDown(int Player, Card Card) : WarEvent;

/// <summary>The battle's winner takes every card in it (in order) to the bottom of their pile.</summary>
public sealed record Won(int Player, IReadOnlyList<Card> Cards) : WarEvent;

/// <summary>The game is over: one player has every card, or (quick game) the battles ran out.</summary>
public sealed record GameOver(int Winner, int[] Cards) : WarEvent;

/// <summary>
/// War for two (the Bicycle rules): the deck is dealt out face down, 26 each. Each battle, both
/// players turn up their top card; the higher rank (aces high) takes both cards to the bottom of
/// their pile. Equal ranks mean war: each player puts one card face down and turns up another,
/// and the higher of those takes everything (wars can repeat). A player who can't turn a card up
/// loses. The classic game is won by taking every card; a quick game ends after a set number of
/// battles, the most cards winning (or a tie).
/// </summary>
public sealed class WarGame
{
	private readonly Queue<Card>[] _piles = { new(), new() };
	private readonly List<Card> _onTable = new();

	public WarGame(IReadOnlyList<Card> deck, int battleLimit = 0)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("War needs a full deck.", nameof(deck));
		Dealt = deck.ToList();
		for (int i = 0; i < deck.Count; i++)
			_piles[i % 2].Enqueue(deck[i]);
		BattleLimit = battleLimit;
	}

	public WarGame(Random random, int battleLimit = 0) : this(Deck.Shuffled(random), battleLimit) { }

	/// <summary>The cards dealt, top first, alternately to player 0 and player 1.</summary>
	public IReadOnlyList<Card> Dealt { get; }

	/// <summary>0 for the classic game (to every card); otherwise how many battles a quick game lasts.</summary>
	public int BattleLimit { get; }

	public int Battles { get; private set; }
	public int Count(int player) => _piles[player].Count;

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
		Battles++;
		int depth = 0;
		while (true)
		{
			// Each turns up their top card; a player who has none loses.
			for (int p = 0; p < 2; p++)
				if (_piles[p].Count == 0)
				{
					End(events, 1 - p, giveTableTo: 1 - p);
					return events;
				}
			var up = new Card[2];
			for (int p = 0; p < 2; p++)
			{
				up[p] = _piles[p].Dequeue();
				_onTable.Add(up[p]);
				events.Add(new Turned(p, up[p]));
			}
			int a = Value(up[0]), b = Value(up[1]);
			if (a != b)
			{
				int winner = a > b ? 0 : 1;
				foreach (var card in _onTable)
					_piles[winner].Enqueue(card);
				events.Add(new Won(winner, _onTable.ToList()));
				_onTable.Clear();
				break;
			}
			// War: one face down each (keeping a card back to turn up, if that's all they have).
			depth++;
			events.Add(new WarDeclared(depth));
			for (int p = 0; p < 2; p++)
				if (_piles[p].Count > 1)
				{
					var down = _piles[p].Dequeue();
					_onTable.Add(down);
					events.Add(new FaceDown(p, down));
				}
		}
		if (_piles[0].Count == 0 || _piles[1].Count == 0)
			End(events, _piles[0].Count > 0 ? 0 : 1, giveTableTo: -1);
		else if (BattleLimit > 0 && Battles >= BattleLimit)
			End(events, _piles[0].Count == _piles[1].Count ? -1 : _piles[0].Count > _piles[1].Count ? 0 : 1, giveTableTo: -1);
		return events;
	}

	private void End(List<WarEvent> events, int winner, int giveTableTo)
	{
		if (giveTableTo >= 0 && _onTable.Count > 0)
		{
			foreach (var card in _onTable)
				_piles[giveTableTo].Enqueue(card);
			events.Add(new Won(giveTableTo, _onTable.ToList()));
			_onTable.Clear();
		}
		IsOver = true;
		Winner = winner;
		events.Add(new GameOver(winner, new[] { _piles[0].Count, _piles[1].Count }));
	}
}

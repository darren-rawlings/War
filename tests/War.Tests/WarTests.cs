using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;
using War.Core;
using Xunit;

namespace War.Tests;

public class WarTests
{
	private static Card C(int rank, Suit suit) => new(rank, suit);

	/// <summary>A deck whose first cards are dealt as given (alternating) and the rest in order.</summary>
	private static List<Card> DeckStarting(params Card[] top) =>
		top.Concat(Deck.Standard().Where(c => !top.Contains(c))).ToList();

	[Fact]
	public void TheDeckIsDealtOutTwentySixEach()
	{
		var game = new WarGame(new Random(1));
		Assert.Equal(26, game.Count(0));
		Assert.Equal(26, game.Count(1));
	}

	[Fact]
	public void TheHigherCardTakesBothAndAcesAreHigh()
	{
		var game = new WarGame(DeckStarting(C(Card.Ace, Suit.Spades), C(Card.King, Suit.Hearts)));
		var events = game.Battle();
		var won = Assert.Single(events.OfType<Won>());
		Assert.Equal(0, won.Player);
		Assert.Equal(27, game.Count(0));
		Assert.Equal(25, game.Count(1));
		// The won cards go to the bottom of the winner's pile.
		Assert.Equal(C(Card.King, Suit.Hearts), game.Pile(0)[^1]);
	}

	[Fact]
	public void EqualCardsMeanWarAndTheNextFaceUpCardDecides()
	{
		// Both turn a 9; then one down each (2♠, 3♠); then 5♥ against Q♥.
		var deck = DeckStarting(C(9, Suit.Clubs), C(9, Suit.Diamonds), C(2, Suit.Spades), C(3, Suit.Spades), C(5, Suit.Hearts), C(12, Suit.Hearts));
		var game = new WarGame(deck);
		var events = game.Battle();
		Assert.Single(events.OfType<WarDeclared>());
		Assert.Equal(2, events.OfType<FaceDown>().Count());
		var won = Assert.Single(events.OfType<Won>());
		Assert.Equal(1, won.Player);
		Assert.Equal(6, won.Cards.Count);
		Assert.Equal(29, game.Count(1));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void ClassicGamesEndWithOnePlayerHoldingEveryCard(int seed)
	{
		var game = new WarGame(new Random(seed));
		int battles = 0;
		while (!game.IsOver && battles++ < 20000)
		{
			game.Battle();
			Assert.Equal(52, game.Count(0) + game.Count(1));
		}
		if (game.IsOver)
		{
			Assert.InRange(game.Winner, 0, 1);
			Assert.Equal(52, game.Count(game.Winner));
		}
	}

	[Fact]
	public void AQuickGameStopsAfterItsBattlesAndTheMostCardsWin()
	{
		var game = new WarGame(new Random(5), battleLimit: 30);
		while (!game.IsOver)
			game.Battle();
		Assert.True(game.Battles <= 30);
		Assert.Equal(52, game.Count(0) + game.Count(1));
		if (game.Winner >= 0)
			Assert.True(game.Count(game.Winner) >= game.Count(1 - game.Winner));
	}
}

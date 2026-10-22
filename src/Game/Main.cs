using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Cards;
using Renegade.Ui;
using War.Core;

namespace War.Game;

/// <summary>
/// War against the computer: the deck is shuffled and dealt out face down, and each battle both
/// glass hands flick their top card face up onto the felt; the higher card's hand sweeps them all
/// under its pile. Equal cards mean war, with a card face down and another face up. Tap your pile
/// to fight the next battle (or let Settings › Flip automatically do it).
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private WarGame? _game;

	protected override string Title => "War";
	protected override string Hint => "Tap your pile to turn a card";
	protected override bool HasLevels => false;
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => _game is { IsOver: false, Battles: > 0 };

	private int BattleLimit => Settings.Extra.TryGetValue("length", out var length) && length == "quick" ? 26 : 0;

	protected override void AddSettings()
	{
		Hud.AddChoice("Game length", new[] { "Every card", "26 battles" }, BattleLimit > 0 ? 1 : 0, choice =>
		{
			Settings.Extra["length"] = choice == 1 ? "quick" : "classic";
			Settings.Save();
			if (!InProgress)
				Run(StartRound());
		});
		Hud.AddToggle("Flip automatically", Flag("autoflip"), on =>
		{
			SetFlag("autoflip", on);
			if (on && !Busy && _game is { IsOver: false })
				Run(Fight());
		});
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_nodes.Clear();
		_game = null;
		SetScore();
		var order = Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		_game = new WarGame(order, BattleLimit);

		// Deal the whole deck, one card at a time, alternately.
		await DealRound(nodes, Enumerable.Range(0, 52).Select(i => i % 2).ToList(), spacing: 0.07f);
		if (round != Round)
			return;
		await Restack(0);
		await Restack(1);
		Hud.SetRestartVisible(true);
		await Ready();
	}

	/// <summary>Lays a player's pile out neatly, top card on top.</summary>
	private async Task Restack(int p)
	{
		var pile = _game!.Pile(p);
		Tween? last = null;
		for (int i = 0; i < pile.Count; i++)
			last = CardMotion.Slide(_nodes[pile[i]], Dealer.InStack(Table.Pile[p], pile.Count - 1 - i), 0.18f);
		await Await(last);
	}

	/// <summary>Waits for your tap, or carries straight on.</summary>
	private async Task Ready()
	{
		SetScore();
		if (_game!.IsOver)
			return;
		if (Demo || Flag("autoflip"))
		{
			await Pause(0.5f);
			await Fight();
			return;
		}
		Busy = false;
		Hud.SetStatus("Tap your pile to turn a card.", Bright);
	}

	private async Task Fight()
	{
		var game = _game!;
		int round = Round;
		Busy = true;
		var events = game.Battle();
		var placed = new int[2];
		var pending = new List<Task>();
		foreach (var e in events)
		{
			if (round != Round)
				return;
			switch (e)
			{
				case Turned turned:
					// Both turn together: start each throw, and wait once both are on their way.
					pending.Add(Put(turned.Player, turned.Card, placed[turned.Player]++, faceUp: true));
					if (pending.Count == 2)
					{
						await Task.WhenAll(pending);
						pending.Clear();
						await Pause(0.45f);
					}
					break;
				case WarDeclared war:
					Say(1, war.Depth == 1 ? "War!" : "War again!");
					Hud.SetStatus(war.Depth == 1 ? "Equal cards: war!" : "Equal again: another war!", Bright);
					await Pause(0.7f);
					break;
				case FaceDown down:
					pending.Add(Put(down.Player, down.Card, placed[down.Player]++, faceUp: false));
					if (pending.Count == 2)
					{
						await Task.WhenAll(pending);
						pending.Clear();
					}
					break;
				case Won won:
					await Task.WhenAll(pending);
					pending.Clear();
					SayNone();
					await Sweep(won.Player, won.Cards);
					break;
				case GameOver over:
					await Task.WhenAll(pending);
					SetScore();
					int w = over.Winner;
					string message = w == 0 ? $"You win the war, {over.Cards[0]} cards to {over.Cards[1]}!"
						: w == 1 ? $"Rosie wins the war, {over.Cards[1]} cards to {over.Cards[0]}."
						: "A tie: 26 cards each!";
					Finish(message, w, w >= 0 ? game.Pile(w).Take(8).Select(c => _nodes[c]) : null, Table.Pile[w >= 0 ? w : 0]);
					return;
			}
		}
		await Ready();
	}

	/// <summary>A player's hand flicks their top card onto the battle, face up or down.</summary>
	private async Task Put(int p, Card card, int k, bool faceUp)
	{
		var node = _nodes[card];
		float z = p == 0 ? 0.25f : -0.95f;
		var at = new Vector3(-0.35f + k * 0.2f, CardTable.FeltY + 0.004f * (k + 1), z);
		float yaw = ((float)Random.NextDouble() - 0.5f) * 0.25f;
		var target = faceUp ? FaceUpAt(at, yaw) : FaceDownAt(at, yaw);
		await Hands[p].Throw(new[] { node }, new[] { target });
		if (faceUp)
			Hud.SetStatus(p == 0 ? $"You turn the {Card.RankName(card.Rank).ToLowerInvariant()}." : $"Rosie turns the {Card.RankName(card.Rank).ToLowerInvariant()}.", Muted);
	}

	/// <summary>The winner's hand gathers the battle's cards and slides them under their pile.</summary>
	private async Task Sweep(int p, IReadOnlyList<Card> cards)
	{
		var top = cards.Select(c => _nodes[c]).OrderByDescending(n => n.GlobalPosition.Y).ToList();
		string what = cards.Count == 2 ? "both cards" : $"all {cards.Count} cards";
		Hud.SetStatus(p == 0 ? $"You take {what}." : $"Rosie takes {what}.", Bright);
		// Gather them beside the pile, face down, then the pile settles with them at the bottom.
		var beside = Table.Pile[p] + new Vector3(p == 0 ? 0.75f : -0.75f, 0, 0);
		var targets = top.Select((_, i) => Dealer.InStack(beside, i)).ToList();
		await Hands[p].Carry(top, targets, seconds: 0.35f, lift: 0.2f);
		await Restack(p);
		SetScore();
	}

	private void SetScore()
	{
		var game = _game;
		Hud.SetScore(game == null ? $"Wins {Wins.First}–{Wins.Second}" : $"Cards: you {game.Count(0)} · {game.Count(1)} Rosie   ·   Wins {Wins.First}–{Wins.Second}");
	}

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_game is not { IsOver: false })
			return;
		if (HitCard(_game.Pile(0).Select(c => _nodes[c]), origin, direction) != null)
		{
			Hud.HideHint();
			Run(Fight());
		}
	}
}

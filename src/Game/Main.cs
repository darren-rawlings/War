using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Cards;
using Renegade.Ui;
using War.Core;

namespace War.Game;

/// <summary>
/// War for two to four: the deck is shuffled and dealt out face down, and each battle every
/// player's glass hand flicks their top card face up onto the felt; the highest card's hand
/// sweeps them all under its pile. A tie for the highest means war: three cards face down each
/// (or one), then another face up. When a war is won, the face-down cards are turned over for a
/// moment so everyone sees what was hidden. Tap your pile to fight the next battle, or let
/// Settings › Flip automatically do it (with a Pause button).
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private WarGame? _game;
	private Button _pause = null!;
	private bool _paused;

	protected override string Title => "War";
	protected override string Hint => "Tap your pile to turn a card";
	protected override bool HasLevels => false;
	protected override bool HoldsFan => false;
	protected override int MinPlayers => 2;
	protected override int MaxPlayers => 4;
	protected override int TalkRounds => 3;
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => _game is { IsOver: false, Battles: > 0 };

	private int BattleLimit => Settings.Extra.TryGetValue("length", out var length) && length == "quick" ? 26 : 0;
	private int FaceDownCount => Settings.Extra.TryGetValue("facedown", out var n) && n == "1" ? 1 : 3;
	private bool AutoFlip => Demo || Flag("autoflip");

	protected override void AddSettings()
	{
		Hud.AddChoice("Game length", new[] { "Every card", "26 battles" }, BattleLimit > 0 ? 1 : 0, choice =>
		{
			Settings.Extra["length"] = choice == 1 ? "quick" : "classic";
			Settings.Save();
			if (!InProgress)
				Run(StartRound());
		});
		Hud.AddChoice("Face down in a war", new[] { "Three cards", "One card" }, FaceDownCount == 1 ? 1 : 0, choice =>
		{
			Settings.Extra["facedown"] = choice == 1 ? "1" : "3";
			Settings.Save();
			if (!InProgress)
				Run(StartRound());
		});
		Hud.AddToggle("Flip automatically", Flag("autoflip"), on =>
		{
			SetFlag("autoflip", on);
			_paused = false;
			UpdatePause();
			if (on && !Busy && _game is { IsOver: false })
				Run(Fight());
		});

		_pause = Hud.AddButton("Pause", false, Control.LayoutPreset.BottomLeft, 20, 180, -186, -130, 22);
		_pause.Pressed += () =>
		{
			_paused = !_paused;
			UpdatePause();
			if (!_paused && !Busy && _game is { IsOver: false })
				Run(Fight());
		};
		UpdatePause();
	}

	/// <summary>The Pause button shows while the battles play themselves; it reads Play while paused.</summary>
	private void UpdatePause()
	{
		_pause.Visible = Flag("autoflip") && _game is { IsOver: false };
		_pause.Text = _paused ? "Play" : "Pause";
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_nodes.Clear();
		_game = null;
		_paused = false;
		UpdatePause();
		SetScore();
		var order = Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		_game = new WarGame(order, BattleLimit, Players, FaceDownCount);

		// Deal the whole deck, one card at a time round the table.
		await DealRound(nodes, Enumerable.Range(0, 52).Select(i => i % Players).ToList(), spacing: 0.07f);
		if (round != Round)
			return;
		await Task.WhenAll(Enumerable.Range(0, Players).Select(Restack));
		Hud.SetRestartVisible(true);
		UpdatePause();
		await NextBattle();
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
	private async Task NextBattle()
	{
		SetScore();
		UpdatePause();
		if (_game!.IsOver)
			return;
		if (AutoFlip && !_paused)
		{
			await Pause(0.5f);
			if (!_paused)
			{
				await Fight();
				return;
			}
		}
		Busy = false;
		Hud.SetStatus(_paused ? "Paused. Tap Play (or your pile) to carry on." : "Tap your pile to turn a card.", Bright);
	}

	private async Task Fight()
	{
		var game = _game!;
		int round = Round;
		Busy = true;
		var events = game.Battle();
		// Tutorial clips start as a battle (or one that ends in a war) begins.
		ClipMoment("battle", 5);
		if (events.Any(e => e is WarDeclared))
			ClipMoment("war", 8);
		var placed = new int[Players];
		var pending = new List<Task>();
		var turned = new List<(int Player, Card Card)>();
		// Each player's hand throws one card at a time; different players throw at once.
		var chain = new Task[Players];
		Task Queue(int p, Card card, bool faceUp)
		{
			int k = placed[p]++;
			var before = chain[p] ?? Task.CompletedTask;
			async Task Next()
			{
				await before;
				await Put(p, card, k, faceUp);
			}
			return chain[p] = Next();
		}
		int group = 0;
		foreach (var e in events)
		{
			if (round != Round)
				return;
			switch (e)
			{
				case Turned t:
					// Everyone in the battle turns together: start each throw, then wait for them all.
					turned.Add((t.Player, t.Card));
					pending.Add(Queue(t.Player, t.Card, faceUp: true));
					if (++group >= Contenders(events, e))
					{
						await Task.WhenAll(pending);
						pending.Clear();
						group = 0;
						await Pause(0.45f);
					}
					break;
				case WarDeclared war:
					string who = string.Join(" and ", war.Players.Select(p => NameOf(p)));
					Say(war.Players.FirstOrDefault(p => p != 0, war.Players[0]), war.Depth == 1 ? "War!" : "War again!");
					Hud.SetStatus(war.Depth == 1 ? $"A tie: war between {who}!" : $"Equal again: another war!", Bright);
					News($"{string.Join(" · ", turned.Select(x => $"{NameOf(x.Player)} {x.Card}"))} → war!");
					turned.Clear();
					await Pause(0.7f);
					break;
				case FaceDown down:
					pending.Add(Queue(down.Player, down.Card, faceUp: false));
					break;
				case Won won:
					await Task.WhenAll(pending);
					pending.Clear();
					SayNone();
					News($"{string.Join(" · ", turned.Select(x => $"{NameOf(x.Player)} {x.Card}"))} → {NameOf(won.Player)}");
					// The winning card has its moment: it rises, swells and sparkles.
					if (turned.LastOrDefault(x => x.Player == won.Player) is { Card: var best } && _nodes.TryGetValue(best, out var bestNode))
						await Glory(bestNode, won.Player);
					if (won.Hidden.Count > 0)
						await Reveal(won);
					await Sweep(won.Player, won.Cards);
					break;
				case Out @out:
					News($"{NameOf(@out.Player)} {(@out.Player == 0 ? "are" : "is")} out of cards.");
					// Out of the game: their hands leave the table.
					Hands[@out.Player].Leave();
					break;
				case GameOver over:
					await Task.WhenAll(pending);
					SetScore();
					UpdatePause();
					int w = over.Winner;
					var counts = string.Join(", ", Enumerable.Range(0, Players).Where(p => over.Cards[p] > 0).Select(p => $"{NameOf(p)} {over.Cards[p]}"));
					string message = w == 0 ? (BattleLimit > 0 ? $"You win the war: {counts}!" : "You win the war, with every card!")
						: w > 0 ? (BattleLimit > 0 ? $"{Names[w]} wins the war: {counts}." : $"{Names[w]} wins the war, with every card.")
						: $"A tie: {counts}!";
					_pause.Visible = false;
					Finish(message, w, w >= 0 ? game.Pile(w).Take(8).Select(c => _nodes[c]) : null, Table.Pile[w >= 0 ? w : 0]);
					return;
			}
		}
		await NextBattle();
	}

	/// <summary>How many cards turn up together at this step of a battle (everyone in it, or those at war).</summary>
	private static int Contenders(List<WarEvent> events, WarEvent at)
	{
		int i = events.IndexOf(at);
		int start = i;
		while (start > 0 && events[start - 1] is Turned)
			start--;
		int end = i;
		while (end + 1 < events.Count && events[end + 1] is Turned)
			end++;
		return end - start + 1;
	}

	/// <summary>Where a player's battle cards go: towards the middle from their pile, side by side.</summary>
	private Vector3 BattleSpot(int p, int k)
	{
		var dir = Table.Toward[p];
		var right = dir.Cross(Vector3.Up);
		// Well clear of the pile (a card's length and more), so nothing lands under it.
		float reach = Mathf.Abs(dir.X) > 0.9f ? 0.85f : 1.0f;
		return Table.Pile[p] + dir * reach + right * (-0.3f + k * 0.16f) + Vector3.Up * (CardTable.FeltY + 0.004f * (k + 1));
	}

	/// <summary>A player's hand flicks their top card onto the battle, face up or down.</summary>
	private async Task Put(int p, Card card, int k, bool faceUp)
	{
		var node = _nodes[card];
		var toward = -Table.Toward[p];
		float yaw = (p == 0 ? 0 : Mathf.Atan2(toward.X, toward.Z)) + ((float)Random.NextDouble() - 0.5f) * 0.25f;
		var at = BattleSpot(p, k);
		var target = faceUp ? FaceUpAt(at, yaw) : FaceDownAt(at, yaw);
		await Hands[p].Throw(new[] { node }, new[] { target });
		if (faceUp)
			Hud.SetStatus(p == 0 ? $"You turn the {Card.RankName(card.Rank).ToLowerInvariant()}." : $"{Names[p]} turns the {Card.RankName(card.Rank).ToLowerInvariant()}.", Muted);
	}

	/// <summary>The winning card's moment: it lifts and swells, sparkling, for a second, then settles back.</summary>
	private async Task Glory(Card3D card, int p)
	{
		var home = card.GlobalPosition;
		var up = home + Vector3.Up * 0.3f;
		string rank = Card.RankName(card.Card.Rank).ToLowerInvariant();
		Hud.SetStatus(p == 0 ? $"Your {rank} wins!" : $"{Names[p]}'s {rank} wins!", Bright);
		var rise = card.CreateTween().SetParallel();
		rise.TweenProperty(card, "global_position", up, 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		rise.TweenProperty(card, "scale", Vector3.One * 1.3f, 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		await Await(rise);
		Renegade.Play.Sparkle.Burst(this, up + Vector3.Up * 0.05f);
		Sounds.PlaySlap(up);
		await Pause(0.8f);
		var settle = card.CreateTween().SetParallel();
		settle.TweenProperty(card, "global_position", home, 0.2f);
		settle.TweenProperty(card, "scale", Vector3.One, 0.2f);
		await Await(settle);
	}

	/// <summary>After a war: the face-down cards turn over for a moment, so the loser sees what was lost.</summary>
	private async Task Reveal(Won won)
	{
		Tween? last = null;
		foreach (var card in won.Hidden)
		{
			var node = _nodes[card];
			var t = node.GlobalTransform;
			var yaw = t.Basis.GetEuler().Y;
			last = CardMotion.Fly(node, FaceUpAt(t.Origin + Vector3.Up * 0.01f, yaw), 0.3f, lift: 0.15f, landed: () => Sounds.PlayFlick(t.Origin));
		}
		await Await(last);
		string hidden = string.Join(", ", won.Hidden.Select(c => c.ToString()));
		News($"Under the war: {hidden}.");
		Hud.SetStatus("Here's what was face down.", Bright);
		await Pause(1.4f);
	}

	/// <summary>The winner's hand gathers the battle's cards and slides them under their pile.</summary>
	private async Task Sweep(int p, IReadOnlyList<Card> cards)
	{
		var top = cards.Select(c => _nodes[c]).OrderByDescending(n => n.GlobalPosition.Y).ToList();
		string what = cards.Count == 2 ? "both cards" : $"all {cards.Count} cards";
		Hud.SetStatus(p == 0 ? $"You take {what}." : $"{Names[p]} takes {what}.", Bright);
		// Gather them beside the pile, face down, then the pile settles with them at the bottom.
		var beside = Table.Pile[p] + Table.Toward[p].Cross(Vector3.Up) * 0.75f;
		var targets = top.Select((_, i) => Dealer.InStack(beside, i)).ToList();
		await Hands[p].Carry(top, targets, seconds: 0.35f, lift: 0.2f);
		await Restack(p);
		SetScore();
	}

	private void SetScore()
	{
		var game = _game;
		if (game == null)
		{
			Hud.SetScore($"Wins {Wins.First}–{Wins.Second}");
			return;
		}
		var counts = Enumerable.Range(0, Players).Select(p => $"{(p == 0 ? "you" : Names[p])} {game.Count(p)}");
		Hud.SetScore($"Cards: {string.Join(" · ", counts)}   ·   Wins {Wins.First}");
	}

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_game is not { IsOver: false })
			return;
		if (HitCard(_game.Pile(0).Select(c => _nodes[c]), origin, direction) != null)
		{
			Hud.HideHint();
			_paused = false;
			UpdatePause();
			Run(Fight());
		}
	}
}

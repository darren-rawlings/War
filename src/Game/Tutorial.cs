using System.Collections.Generic;
using Renegade.Ui;

namespace War.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Battle",
			"The deck is dealt out face down round the table (two to four players: Settings › Players). Tap your pile: everyone turns their top card face up. The highest card (aces are high) takes them all, under its pile.",
			"res://tutorial/battle.ogv"),
		new TutorialPage("War!",
			"A tie for the highest card means war between those players: each puts three cards face down (or one: Settings), then turns another face up. The highest of those takes every card on the table, and the face-down cards are turned over so everyone sees what was hidden.",
			"res://tutorial/war.ogv"),
		new TutorialPage("Winning",
			"Take every card to win the war. In a hurry? Settings › Game length plays 26 battles, and the most cards win. Settings › Flip automatically plays the battles for you, with a Pause button. Settings › Speed makes everything faster.",
			"res://tutorial/battle.ogv"),
	};
}

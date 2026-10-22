using System.Collections.Generic;
using Renegade.Ui;

namespace War.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Battle",
			"The deck is dealt out face down between you and Rosie. Tap your pile: you both turn your top card face up. The higher card (aces are high) takes both, under its pile."),
		new TutorialPage("War!",
			"Equal cards mean war: you each put a card face down, then turn another face up. The higher of those takes every card on the table. Wars can happen twice in a row."),
		new TutorialPage("Winning",
			"Take every card to win the war. In a hurry? Settings › Game length plays 26 battles, and the most cards win. Settings › Flip automatically plays the battles for you."),
	};
}

"""
Writes the training mode's content: the two training manuscripts and the iAA training charts.

    python Tools/Training/make_training_content.py        (needs matplotlib and the Liberation Serif font)

Outputs (paths from the repository root):
    Assets/Resources/Manuscripts/Training/iAT.json, iAA.json
    Assets/StreamingAssets/Augmentation Images/Training/Group/Training_iAA_Q1_Grp_*.jpg
    Assets/StreamingAssets/Augmentation Images/Training/Individual/Training_iAA_Q1_A<n>_Ind_Seating.jpg

The manuscripts have the same layout as the study's (Resources/Manuscripts/*.json), so SessionController and the
cards read them unchanged. Every stat and chart is worked out from CONVERSATIONS below, so they always agree with
what the avatars say. The topic is unrelated to the study's, every avatar answers the question once in two
sentences, and no one agrees or disagrees with anyone else.

The charts copy the study charts' size, layout, font and colours. SPEECH_SECONDS was measured with piper-tts and
the training voices at their calibrated lengthScale (see Tools/Training/TrainingTools.cs).
"""

import json
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D
from matplotlib.patches import Circle, Rectangle

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MANUSCRIPT_DIR = os.path.join(ROOT, "Assets", "Resources", "Manuscripts", "Training")
IMAGE_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Augmentation Images", "Training")
IMAGE_PATH = "Augmentation Images/Training"  # as written in the manuscripts: relative to StreamingAssets

QUESTION = "How was the weather on your last vacation?"
RANKS = ["Very Bad", "Bad", "Neutral", "Good", "Very Good"]  # left to right on the charts
SHORT = {"Very Bad": "VB", "Bad": "B", "Neutral": "N", "Good": "G", "Very Good": "VG"}
EMOTION = {"Very Good": "Satisfied", "Good": "Satisfied", "Neutral": "Neutral", "Bad": "Disappointed",
           "Very Bad": "Disappointed"}

CONVERSATIONS = {
    "iAT": {
        "order_Seating": [3, 1, 4, 2],
        "order_Speech": [2, 3, 1, 4],
        "agents": {
            1: ("Good",
                "It was mostly sunny with a light breeze, which was nice for walking around town. We had one rainy evening, but it cleared by morning.",
                "■ Mostly sunny with a light breeze. \n ■ One rainy evening cleared by morning."),
            2: ("Bad",
                "It rained on four of our six days at the lake. We spent most afternoons indoors playing board games instead of swimming.",
                "■ Rain on four of six days at the lake. \n ■ Afternoons spent indoors instead of swimming."),
            3: ("Very Good",
                "We had clear skies and warm days for the whole week in the mountains. Every hike we planned had wide views from start to finish.",
                "■ Clear skies and warm days all week. \n ■ Wide views on every hike."),
            4: ("Neutral",
                "The weather on the coast changed a lot from day to day. Some mornings were foggy and cool, and some afternoons were hot and bright.",
                "■ Weather on the coast changed day to day. \n ■ Foggy mornings and hot afternoons."),
        },
        "suggestions": "■ Did the time of year matter? \n ■ How did the weather change your plans?",
        "themes": "■ Rain Limited Outdoor Plans \n ■ Clear Days Suited Time Outside",
    },
    "iAA": {
        "order_Seating": [2, 4, 1, 3],
        "order_Speech": [3, 1, 4, 2],
        "agents": {
            1: ("Neutral",
                "It was cloudy most of the time in the city, but it hardly rained. That was fine for museums, though not great for photos.",
                "■ Cloudy most days in the city, with little rain. \n ■ Fine for museums, not for photos."),
            2: ("Very Good",
                "We had bright sun and calm water every day at the beach. It stayed warm enough to swim from early morning until sunset.",
                "■ Bright sun and calm water every day. \n ■ Warm enough to swim until sunset."),
            3: ("Very Bad",
                "A storm hit on our second day and cancelled the ferry to the island. The rest of the trip was windy, wet and cold.",
                "■ A storm cancelled the ferry to the island. \n ■ The rest of the trip was windy, wet and cold."),
            4: ("Good",
                "The days in the countryside were warm and dry, and the nights were pleasantly cool. Only the last afternoon had a short shower.",
                "■ Warm, dry days and cool nights. \n ■ Only one short shower."),
        },
        "suggestions": "■ Was the weather what you expected? \n ■ Which kinds of trips suit cloudy days?",
        "themes": "■ Storms and Rain Cut Plans Short \n ■ Warm, Dry Days Suited Being Outdoors",
    },
}

# Seconds of speech per line (agent -> seconds), measured with piper-tts and the training voices.
SPEECH_SECONDS = {
    "iAT": {1: 6.3, 2: 6.4, 3: 6.2, 4: 6.7},
    "iAA": {1: 5.5, 2: 6.1, 3: 6.2, 4: 6.5},
}


def minutes_label(seconds):
    """dur_Spoken as the study writes it: "<1" under a minute, otherwise "~n" minutes."""
    return "<1" if seconds < 60 else f"~{round(seconds / 60)}"


def image_name(condition, kind):
    return f"Training_{condition}_Q1_{kind}.jpg"


def manuscript(condition, conv):
    agents = conv["agents"]
    speech = conv["order_Speech"]
    first_speaker, first_seated = speech[0], conv["order_Seating"][0]
    with_images = condition == "iAA"  # the iAT cards and wall show no charts

    def path(folder, kind):
        return f"{IMAGE_PATH}/{folder}/{image_name(condition, kind)}" if with_images else None

    question = {
        "q1_Txt": QUESTION,
        "apr_Positions": dict(
            [("order_Seating", conv["order_Seating"])] +
            [(f"agent_{a}_Pos", agents[a][0]) for a in sorted(agents)]),
        "glb_Responses": dict(
            [("order_Speech", speech), ("emotions", [EMOTION[agents[a][0]] for a in sorted(agents)])] +
            [(f"agent_{a}_Resp", agents[a][1]) for a in speech]),
        "glb_Augmentations": {
            "glb_ind_Sums": {f"agent_{a}_Sum": agents[a][2] for a in sorted(agents)},
            "glb_Grp_Sums": {"glb_Grp_Suggestions": conv["suggestions"], "glb_Emg_Themes": conv["themes"]},
        },
        "apr_Augmentations": {
            "apr_Ind_Stats": {
                f"agent_{a}_apr_Stats": {
                    "times_Spoken_First": int(a == first_speaker),
                    "times_Seated_First": int(a == first_seated),
                    "ind_Seating": path("Individual", f"A{a}_Ind_Seating"),
                } for a in sorted(agents)},
            "apr_Grp_Sums": {
                "speaking_Sum": path("Group", "Grp_Speaking_Sum"),
                "grp_Move": path("Group", "Grp_Group_Move"),
                "sim_Mat": path("Group", "Grp_Sim_Mat"),
            },
        },
        "trd_Augmentations": {
            "trd_Ind_Stats": {
                f"agent_{a}_trd_Stats": {
                    "dur_Spoken": minutes_label(SPEECH_SECONDS[condition][a]),
                    "times_Spoken": speech.count(a),
                    "words_Spoken": len(agents[a][1].split()),
                } for a in sorted(agents)},
            "trd_Grp_Sum": {
                # Turn-taking: each time the floor passes from one speaker to a different one.
                "turn_Taking": sum(1 for x, y in zip(speech, speech[1:]) if x != y),
                # Dissonant opinions: explicit counter-statements to an earlier speaker; no avatar makes one.
                "no_Diss_Ops": 0,
            },
        },
    }
    return {"question_1_Data": question}


# --- Charts: same pixel layout as the study's (900 x 900 grids, 3000 x 2000 line chart) ---

plt.rcParams["font.family"] = "Liberation Serif"
INK = "black"
GREY = "#aeaaa9"   # header bands of the similarity matrix
LIGHT = "#c7c7c7"  # inner lines of the similarity matrix
GRID = "#d9d9d9"   # horizontal grid of the line chart
FILL = "#ffc000"   # the seat on an individual seating chart


def canvas(width=900, height=900):
    """A figure whose data coordinates are pixels, origin top left."""
    fig = plt.figure(figsize=(width / 100, height / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_xlim(0, width)
    ax.set_ylim(height, 0)
    ax.axis("off")
    return fig, ax


def save(fig, path, dpi=100):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    fig.savefig(path, dpi=dpi, facecolor="white", pil_kwargs={"quality": 95})
    plt.close(fig)


def grid_lines(ax, x0, y0, cols, rows, cell):
    for c in range(cols + 1):
        ax.plot([x0 + c * cell, x0 + c * cell], [y0, y0 + rows * cell], color=INK, lw=2.9, solid_capstyle="projecting")
    for r in range(rows + 1):
        ax.plot([x0, x0 + cols * cell], [y0 + r * cell, y0 + r * cell], color=INK, lw=2.9, solid_capstyle="projecting")


def group_move(conv, path):
    """Where each avatar stood: its rating's column, stacked from the bottom row in seating order."""
    fig, ax = canvas()
    x0, y0, cell = 49.5, 74.5, 160
    grid_lines(ax, x0, y0, 5, 4, cell)
    stacked = {rank: 0 for rank in RANKS}
    for a in conv["order_Seating"]:
        rank = conv["agents"][a][0]
        col, row = RANKS.index(rank), 3 - stacked[rank]
        stacked[rank] += 1
        ax.text(x0 + (col + 0.5) * cell, y0 + (row + 0.5) * cell, f"P.{a}", ha="center", va="center", fontsize=62)
    for col, rank in enumerate(RANKS):
        ax.text(x0 + (col + 0.5) * cell, 772, SHORT[rank], ha="center", va="center", fontsize=70)
    save(fig, path)


def similarity_matrix(conv, path):
    """A dot where the row's avatar spoke and the column's avatar spoke next."""
    fig, ax = canvas()
    ax.add_patch(Rectangle((152, 67), 686, 86, color=GREY, lw=0))          # Follower band
    ax.add_patch(Rectangle((62, 237), 86, 596, color=GREY, lw=0))          # Starter band
    ax.add_patch(Rectangle((152, 157), 86, 76, color=GREY, lw=0))          # corner
    ax.text(495, 108, "Follower", ha="center", va="center", fontsize=48)
    ax.text(105, 535, "Starter", ha="center", va="center", fontsize=48, rotation=90)
    for i in range(4):
        ax.add_patch(Rectangle((242 + 150 * i, 157), 146, 76, color=GREY, lw=0))
        ax.add_patch(Rectangle((152, 237 + 150 * i), 86, 146, color=GREY, lw=0))
        ax.text(315 + 150 * i, 195, f"P.{i + 1}", ha="center", va="center", fontsize=44)
        ax.text(195, 310 + 150 * i, f"P.{i + 1}", ha="center", va="center", fontsize=44)
    for i in (1, 2, 3):
        ax.plot([240 + 150 * i] * 2, [235, 835], color=LIGHT, lw=2.2)
        ax.plot([240, 840], [235 + 150 * i] * 2, color=LIGHT, lw=2.2)
    ax.add_patch(Rectangle((239.5, 234.5), 600.5, 600.5, fill=False, ec=INK, lw=2.9))
    speech = conv["order_Speech"]
    for starter, follower in zip(speech, speech[1:]):
        if starter != follower:
            ax.add_patch(Circle((240 + 150 * (follower - 0.5), 235 + 150 * (starter - 0.5)), 14.5, color=INK))
    save(fig, path)


def speaking_summary(condition, conv, path):
    """Turns (encounters) and seconds of speech per avatar."""
    agents = sorted(conv["agents"])
    turns = [conv["order_Speech"].count(a) for a in agents]
    seconds = [SPEECH_SECONDS[condition][a] for a in agents]
    fig = plt.figure(figsize=(10, 20 / 3), dpi=300)
    ax = fig.add_axes([0.1048, 0.185, 0.755, 0.664])
    right = ax.twinx()
    ax.set_ylim(0, 6)
    right.set_ylim(0, 180)
    ax.set_yticks(range(7))
    right.set_yticks(range(0, 181, 30))
    ax.set_xlim(-0.1, 3.1)
    ax.set_xticks(range(4))
    ax.set_xticklabels([f"P{a}" for a in agents])
    ax.grid(axis="y", color=GRID, lw=1.3)
    ax.set_axisbelow(True)
    for spine in ("left", "bottom"):
        ax.spines[spine].set_linewidth(2.9)
    right.spines["right"].set_linewidth(2.9)
    for a in (ax, right):
        a.spines["top"].set_visible(False)
        a.tick_params(labelsize=28, width=0, length=0, pad=10)
    right.spines["left"].set_visible(False)
    right.spines["bottom"].set_visible(False)
    ax.plot(range(4), turns, color=INK, lw=4.3, marker="o", markersize=15, clip_on=False, zorder=3)
    right.plot(range(4), seconds, color=INK, lw=4.3, ls=(0, (3, 1.5)), marker="^", markersize=15, clip_on=False, zorder=3)
    ax.set_xlabel("Participants", fontsize=33, labelpad=12)
    ax.set_ylabel("Encounters (#)", fontsize=33, labelpad=14)
    right.set_ylabel("Speaking Duration (s)", fontsize=33, labelpad=14)
    handles = [Line2D([], [], color=INK, lw=4.3, marker="o", markersize=15),
               Line2D([], [], color=INK, lw=4.3, ls=(0, (3, 1.5)), marker="^", markersize=15)]
    fig.legend(handles, ["Encounters", "Speaking Duration"], loc="upper center", bbox_to_anchor=(0.5, 0.985), ncol=2,
               frameon=False, fontsize=33, handlelength=2.6, columnspacing=3)
    save(fig, path, dpi=300)


def individual_seating(conv, agent, path):
    """One row per question (#1-#4, as in the study); the training has one question, so only #1 is filled."""
    fig, ax = canvas()
    x0, y0, cell = 119.5, 179.5, 150
    col = RANKS.index(conv["agents"][agent][0])
    ax.add_patch(Rectangle((x0 + col * cell, y0), cell, cell, color=FILL, lw=0))
    grid_lines(ax, x0, y0, 5, 4, cell)
    for c, rank in enumerate(RANKS):
        ax.text(x0 + (c + 0.5) * cell, 118, SHORT[rank], ha="center", va="center", fontsize=56)
    for r in range(4):
        ax.text(52, y0 + (r + 0.5) * cell, f"#{r + 1}", ha="center", va="center", fontsize=56)
    save(fig, path)


def main():
    os.makedirs(MANUSCRIPT_DIR, exist_ok=True)
    for condition, conv in CONVERSATIONS.items():
        with open(os.path.join(MANUSCRIPT_DIR, f"{condition}.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(manuscript(condition, conv), f, indent=4, ensure_ascii=False)
            f.write("\n")

    conv = CONVERSATIONS["iAA"]
    group_move(conv, os.path.join(IMAGE_DIR, "Group", image_name("iAA", "Grp_Group_Move")))
    similarity_matrix(conv, os.path.join(IMAGE_DIR, "Group", image_name("iAA", "Grp_Sim_Mat")))
    speaking_summary("iAA", conv, os.path.join(IMAGE_DIR, "Group", image_name("iAA", "Grp_Speaking_Sum")))
    for agent in sorted(conv["agents"]):
        individual_seating(conv, agent, os.path.join(IMAGE_DIR, "Individual", image_name("iAA", f"A{agent}_Ind_Seating")))
    print("Wrote the training manuscripts and charts.")


if __name__ == "__main__":
    main()

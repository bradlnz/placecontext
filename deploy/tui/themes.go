package main

import "github.com/charmbracelet/lipgloss"

// A theme is a named colour palette. applyTheme rebuilds every style var (and the scene palette) from
// it, so cycling themes ([c]) restyles the whole UI. themes[0] matches the original default colours.
type theme struct {
	name                                                             string
	accent, worker, ok, warn, err, gray, dim, selBG, link, db, pulse lipgloss.Color
}

var themes = []theme{
	{"teal", "44", "39", "42", "220", "196", "245", "239", "24", "238", "141", "51"},
	{"amber", "214", "180", "154", "226", "203", "244", "238", "94", "237", "213", "229"},
	{"matrix", "46", "35", "40", "226", "196", "108", "238", "22", "236", "51", "118"},
	{"synthwave", "201", "75", "49", "220", "198", "104", "240", "53", "238", "213", "51"},
	{"mono", "252", "248", "250", "250", "246", "245", "240", "238", "237", "250", "255"},
}

// bannerFonts are distinct ASCII "fonts" for the PlaceContext logo; the theme picks one, so cycling
// the theme ([c]) also changes the banner font.
var bannerFonts = []string{
	// 0 — small slant
	`  ___ _              ___         _           _
 | _ \ |__ _ __ ___ / __|___ _ _| |_ _____ _| |_
 |  _/ / _' / _/ -_) (__/ _ \ ' \  _/ -_) \ /  _|
 |_| |_\__,_\__\___|\___\___/_||_\__\___/_\_\\__|`,
	// 1 — block (half-blocks)
	`█▀█ █   █▀█ █▀▀ █▀▀ █▀▀ █▀█ █▄ █ ▀█▀ █▀▀ ▀▄▀ ▀█▀
█▀▀ █▄▄ █▀█ █▄▄ ██▄ █▄▄ █▄█ █ ▀█  █  ██▄ █ █  █ `,
	// 2 — heavy
	`╔═╗ ╦  ╔═╗ ╔═╗ ╔═╗ ╔═╗ ╔═╗ ╔╗╔ ╔╦╗ ╔═╗ ═╗ ╦ ╔╦╗
╠═╝ ║  ╠═╣ ║   ║╣  ║   ║ ║ ║║║  ║  ║╣  ╔╩╦╝  ║
╩   ╩  ╩ ╩ ╚═╝ ╚═╝ ╚═╝ ╚═╝ ╝╚╝  ╩  ╚═╝ ╩ ╚═  ╩`,
	// 3 — dotted / lowercase
	`._ |  _.  _ ._ _ _ ._ _|_ _ _|_
|_)|_(_| (_|(/_(_(_)| | |_(/_>< |_   placecontext`,
	// 4 — minimal
	`P L A C E C O N T E X T
· hosted context · mcp + portal ·`,
}

var themeIdx = 0

func applyTheme(i int) {
	t := themes[((i%len(themes))+len(themes))%len(themes)]
	cTeal, cGreen, cYellow, cRed, cGray, cDim = t.accent, t.ok, t.warn, t.err, t.gray, t.dim
	banner = bannerFonts[((i%len(bannerFonts))+len(bannerFonts))%len(bannerFonts)] // theme also picks the font

	bannerStyle = lipgloss.NewStyle().Foreground(cTeal).Bold(true)
	titleStyle = lipgloss.NewStyle().Foreground(cTeal).Bold(true)
	dimStyle = lipgloss.NewStyle().Foreground(cGray)
	okStyle = lipgloss.NewStyle().Foreground(cGreen).Bold(true)
	warnStyle = lipgloss.NewStyle().Foreground(cYellow).Bold(true)
	errStyle = lipgloss.NewStyle().Foreground(cRed).Bold(true)
	boxStyle = lipgloss.NewStyle().Border(lipgloss.RoundedBorder()).BorderForeground(cDim).Padding(0, 1)
	keyStyle = lipgloss.NewStyle().Foreground(cTeal).Bold(true)
	headStyle = lipgloss.NewStyle().Foreground(cGray).Bold(true).Underline(true)
	selStyle = lipgloss.NewStyle().Background(t.selBG).Foreground(lipgloss.Color("231")).Bold(true)

	scenePalette = []lipgloss.Style{
		lipgloss.NewStyle().Foreground(t.accent).Bold(true), // 0 server
		lipgloss.NewStyle().Foreground(t.worker).Bold(true), // 1 worker
		lipgloss.NewStyle().Foreground(t.ok).Bold(true),     // 2 pod ok
		lipgloss.NewStyle().Foreground(t.warn).Bold(true),   // 3 pod pending
		lipgloss.NewStyle().Foreground(t.db).Bold(true),     // 4 db
		lipgloss.NewStyle().Foreground(t.link),              // 5 link
		lipgloss.NewStyle().Foreground(t.pulse).Bold(true),  // 6 data pulse
		// 7–9: the Earth globe — deliberately theme-independent (oceans stay blue,
		// land green, ice white, whatever accent the theme picks). Keep in sync with
		// planet() in cluster.go: it panics the renderer if these ids are missing.
		lipgloss.NewStyle().Foreground(lipgloss.Color("33")),             // 7 ocean
		lipgloss.NewStyle().Foreground(lipgloss.Color("76")).Bold(true),  // 8 land
		lipgloss.NewStyle().Foreground(lipgloss.Color("255")).Bold(true), // 9 polar ice
	}
}

func themeName() string { return themes[((themeIdx%len(themes))+len(themes))%len(themes)].name }

package main

import (
	"fmt"
	"math"
	"strings"

	"github.com/charmbracelet/lipgloss"
)

// scenePalette maps a colour id → style.
// 0 server, 1 worker, 2 pod-ok, 3 pod-pending, 4 db, 5 link.
var scenePalette = []lipgloss.Style{
	lipgloss.NewStyle().Foreground(lipgloss.Color("44")).Bold(true),  // server (teal)
	lipgloss.NewStyle().Foreground(lipgloss.Color("39")).Bold(true),  // worker (blue)
	lipgloss.NewStyle().Foreground(lipgloss.Color("42")).Bold(true),  // pod ok (green)
	lipgloss.NewStyle().Foreground(lipgloss.Color("220")).Bold(true), // pod pending (yellow)
	lipgloss.NewStyle().Foreground(lipgloss.Color("141")).Bold(true), // db (magenta)
	lipgloss.NewStyle().Foreground(lipgloss.Color("238")),            // link (faint)
}

// canvas is a coloured character grid for drawing the topology (lines + markers + labels).
type canvas struct {
	w, h int
	ch   []rune
	col  []int
}

func newCanvas(w, h int) *canvas {
	c := &canvas{w: w, h: h, ch: make([]rune, w*h), col: make([]int, w*h)}
	for i := range c.ch {
		c.ch[i] = ' '
		c.col[i] = -1
	}
	return c
}
func (c *canvas) set(x, y int, r rune, color int) {
	if x >= 0 && x < c.w && y >= 0 && y < c.h {
		c.ch[y*c.w+x] = r
		c.col[y*c.w+x] = color
	}
}
func (c *canvas) text(x, y int, s string, color int) {
	for i, r := range []rune(s) {
		c.set(x+i, y, r, color)
	}
}

// box draws a labelled rectangle centred on (cxp,cyp) — a little "computer" icon for a
// cluster item. The interior is blanked so connecting lines don't bleed through.
func (c *canvas) box(cxp, cyp int, label string, color int) {
	r := []rune(label)
	iw := len(r)
	w := iw + 2
	x0 := cxp - w/2
	y0 := cyp - 1
	c.set(x0, y0, '┌', color)
	c.set(x0+w-1, y0, '┐', color)
	c.set(x0, y0+2, '└', color)
	c.set(x0+w-1, y0+2, '┘', color)
	for i := 1; i < w-1; i++ {
		c.set(x0+i, y0, '─', color)
		c.set(x0+i, y0+2, '─', color)
	}
	c.set(x0, y0+1, '│', color)
	c.set(x0+w-1, y0+1, '│', color)
	c.text(x0+1, y0+1, label, color)
}

// line draws an edge with Bresenham using a single steady glyph ('·'), so a link does not
// flicker/distort between ─│╱╲ as its slope changes while items orbit. It won't overwrite a
// non-link cell, so markers/labels drawn earlier stay legible.
func (c *canvas) line(x0, y0, x1, y1, color int) {
	dx, dy := x1-x0, y1-y0
	ax, ay := abs(dx), abs(dy)
	sx, sy := sgn(dx), sgn(dy)
	err := ax - ay
	x, y := x0, y0
	for {
		if x >= 0 && x < c.w && y >= 0 && y < c.h && c.col[y*c.w+x] == -1 {
			c.ch[y*c.w+x] = '·'
			c.col[y*c.w+x] = color
		}
		if x == x1 && y == y1 {
			break
		}
		e2 := 2 * err
		if e2 > -ay {
			err -= ay
			x += sx
		}
		if e2 < ax {
			err += ax
			y += sy
		}
	}
}

func (c *canvas) String() string {
	var b strings.Builder
	for y := 0; y < c.h; y++ {
		x := 0
		for x < c.w {
			col := c.col[y*c.w+x]
			j := x
			var run []rune
			for j < c.w && c.col[y*c.w+j] == col {
				run = append(run, c.ch[y*c.w+j])
				j++
			}
			if col < 0 {
				b.WriteString(string(run))
			} else {
				b.WriteString(scenePalette[col].Render(string(run)))
			}
			x = j
		}
		b.WriteByte('\n')
	}
	return b.String()
}

func center(s string, width int) string {
	r := []rune(s)
	if len(r) >= width {
		return string(r[:width])
	}
	left := (width - len(r)) / 2
	return strings.Repeat(" ", left) + s + strings.Repeat(" ", width-len(r)-left)
}

// planet draws a shaded ASCII 3D sphere centred on (cxp,cyp) with vertical radius ry — the
// control-plane "planet" the satellites orbit. Each surface point is lit by a fixed light so
// it reads as a 3D ball; the label sits just below it. Horizontal radius is 2×ry to offset
// the ~2:1 cell aspect ratio so it looks round.
func (c *canvas) planet(cxp, cyp, ry int, color int, label string, spin float64) {
	ramp := []rune(".,:;+*oO#@")
	lx, ly, lz := -0.5, -0.6, 0.62
	ln := math.Sqrt(lx*lx + ly*ly + lz*lz)
	lx, ly, lz = lx/ln, ly/ln, lz/ln
	rx := ry * 2
	for dy := -ry; dy <= ry; dy++ {
		for dx := -rx; dx <= rx; dx++ {
			nx := float64(dx) / float64(rx)
			ny := float64(dy) / float64(ry)
			d2 := nx*nx + ny*ny
			if d2 > 1 {
				continue
			}
			nz := math.Sqrt(1 - d2)
			lum := nx*lx + ny*ly + nz*lz
			if lum < 0 {
				lum = 0
			}
			lum = 0.12 + 0.88*lum // ambient so the dark side isn't blank
			i := int(lum * float64(len(ramp)-1))
			// rotating surface texture: longitude advances with `spin`, so continents sweep
			// across the disc and the ball visibly rotates about its vertical axis.
			lon := math.Atan2(nx, nz) + spin
			lat := math.Asin(ny)
			cont := math.Sin(2*lon)*math.Cos(3*lat) + 0.6*math.Sin(4*lon+1.7)*math.Cos(2*lat)
			if cont > 0.5 {
				i += 3 // land: denser glyph
			}
			if i < 0 {
				i = 0
			} else if i >= len(ramp) {
				i = len(ramp) - 1
			}
			c.set(cxp+dx, cyp+dy, ramp[i], color)
		}
	}
	c.text(cxp-len([]rune(label))/2, cyp+ry+1, label, color)
}

// trimToBox returns the point on the border of a box (centre cx,cy, half-extents hw,hh)
// in the direction of (tx,ty) — so link lines start/end at the box edge, not its centre.
func trimToBox(cx, cy, hw, hh, tx, ty int) (int, int) {
	dx, dy := float64(tx-cx), float64(ty-cy)
	if dx == 0 && dy == 0 {
		return cx, cy
	}
	s := math.Inf(1)
	if dx != 0 {
		s = math.Min(s, float64(hw)/math.Abs(dx))
	}
	if dy != 0 {
		s = math.Min(s, float64(hh)/math.Abs(dy))
	}
	return cx + int(dx*s), cy + int(dy*s)
}

func shortName(s string) string {
	s = strings.TrimPrefix(s, "k3d-"+cluster+"-")
	s = strings.TrimPrefix(s, "placecontext-")
	return s
}

// podLabel keeps pod boxes compact: "db" for the database, otherwise the unique
// suffix (the bit after the last hyphen) so replicas stay distinguishable.
func podLabel(name string) string {
	if strings.HasPrefix(name, "placecontext-db") {
		return "db"
	}
	if i := strings.LastIndex(name, "-"); i >= 0 && i < len(name)-1 {
		return name[i+1:]
	}
	return shortName(name)
}

// cluster3DView renders the live cluster as an animated 3D system-topology graph: the
// control-plane and workers laid out in a rotating ring, each pod linked to its node by
// a line, every entity labelled with its (shortened) name.
func (m model) clusterPanel(rows int) string {
	if !m.state.reach {
		return "  " + warnStyle.Render("● no cluster") + dimStyle.Render("   press [u] to bring it up") + "\n"
	}
	w, h := m.w-2, rows
	if w < 30 {
		w = 80
	}
	if h < 7 {
		h = 7
	}
	cv := newCanvas(w, h)

	var servers, workers []nodeRow
	for _, n := range m.state.nodes {
		if n.Role == "server" {
			servers = append(servers, n)
		} else {
			workers = append(workers, n)
		}
	}
	orb := m.orbit
	cx, cy := w/2, h/2

	// planet (clean circle, centred); horizontal radius 2× vertical to offset the ~2:1 cell aspect
	planetRy := (h - 3) / 2
	if planetRy > 5 {
		planetRy = 5
	} else if planetRy < 3 {
		planetRy = 3
	}
	planetRx := planetRy * 2

	// satellites sit on a tight ellipse kept 20–40 columns from the hub, so they never overlap
	// the planet and the layout stays compact.
	orbX := 30.0
	if mx := float64(cx - planetRx - 8); orbX > mx {
		orbX = mx
	}
	if orbX < 20 {
		orbX = 20
	} else if orbX > 40 {
		orbX = 40
	}
	orbY := orbX * 0.36
	if my := float64(cy) - 2; orbY > my {
		orbY = my
	}
	podX := math.Min(orbX+10, 40)
	podY := podX * 0.36
	if my := float64(cy) - 1; podY > my {
		podY = my
	}

	type vis struct {
		sx, sy, hw int
		label      string
		marker     rune
		color      int
	}
	var ents []vis

	// worker angle around the hub (advances with orb → orbital motion)
	nw := max(1, len(workers))
	wAngle := map[string]float64{}
	wpos := map[string][2]int{}
	for i, wk := range workers {
		th := 2*math.Pi*float64(i)/float64(nw) + orb
		wAngle[wk.Name] = th
		x := cx + int(orbX*math.Cos(th))
		y := cy + int(orbY*math.Sin(th))
		wpos[wk.Name] = [2]int{x, y}
	}

	// faint orbital path
	if len(workers) > 0 {
		for a := 0; a < 160; a++ {
			th := 2 * math.Pi * float64(a) / 160
			ox := cx + int(orbX*math.Cos(th))
			oy := cy + int(orbY*math.Sin(th))
			if ox >= 0 && ox < w && oy >= 0 && oy < h && cv.col[oy*w+ox] == -1 {
				cv.set(ox, oy, '·', 5)
			}
		}
	}

	// hub ↔ worker links (worker edge → planet edge), drawn before the boxes/planet
	for _, wk := range workers {
		p := wpos[wk.Name]
		bw := len([]rune("● "+shortName(wk.Name))) + 2
		ax, ay := trimToBox(p[0], p[1], bw/2, 1, cx, cy)
		bx, by := trimToBox(cx, cy, planetRx, planetRy, p[0], p[1])
		cv.line(ax, ay, bx, by, 5)
		col := 1
		if wk.Status != "Ready" {
			col = 3
		}
		ents = append(ents, vis{p[0], p[1], bw / 2, shortName(wk.Name), '●', col})
	}

	// pods on an outer ring near their node's angle, linked to it
	count := map[string]int{}
	for _, p := range m.state.pods {
		count[p.Node]++
	}
	idx := map[string]int{}
	for _, p := range m.state.pods {
		k := idx[p.Node]
		idx[p.Node]++
		base, isWorker := wAngle[p.Node]
		if !isWorker {
			base = orb * 1.3 // server's pods cluster on one arc
		}
		// offset pods off their node's radial (so a single pod doesn't sit on top of the worker box)
		spread := (float64(k)-float64(count[p.Node]-1)/2)*0.5 + 0.34
		th := base + spread
		px := cx + int(podX*math.Cos(th))
		py := cy + int(podY*math.Sin(th))
		col, mark := 2, '•'
		if !(p.Ready == "1/1" && p.Status == "Running") {
			col, mark = 3, '◦'
		}
		if strings.HasPrefix(p.Name, "placecontext-db") {
			col, mark = 4, '◆'
		}
		lab := podLabel(p.Name)
		bw := len([]rune(string(mark)+" "+lab)) + 2
		// link pod → its node (worker box, or the hub for server pods)
		nx, ny, nr, nh := cx, cy, planetRx, planetRy
		if wp, ok := wpos[p.Node]; ok {
			wb := len([]rune("● "+shortName(p.Node)))/2 + 1
			nx, ny, nr, nh = wp[0], wp[1], wb, 1
		}
		ax, ay := trimToBox(px, py, bw/2, 1, nx, ny)
		bx, by := trimToBox(nx, ny, nr, nh, px, py)
		cv.line(ax, ay, bx, by, 5)
		ents = append(ents, vis{px, py, bw / 2, lab, mark, col})
	}

	// planet on top of the links so its circle stays intact; satellites are well clear of it
	for _, s := range servers {
		cv.planet(cx, cy, planetRy, 0, shortName(s.Name), orb*1.6)
		break // single hub
	}
	for _, e := range ents {
		cv.box(e.sx, e.sy, string(e.marker)+" "+e.label, e.color)
	}

	spin := "on"
	if !m.clSpin {
		spin = "off"
	}
	var b strings.Builder
	leg := func(c int, s string) string { return scenePalette[c].Render(s) }
	b.WriteString(titleStyle.Render(" cluster ") + "  " +
		leg(0, "◆ server") + " " + leg(1, "● worker") + " " + leg(2, "• pod") + " " + leg(4, "◆ db") +
		dimStyle.Render(fmt.Sprintf("   spin:%s zoom:%.1f×", spin, m.clZoom)) + "\n")
	b.WriteString(cv.String())
	return b.String()
}

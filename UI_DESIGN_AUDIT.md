# Woven — UI Design Audit
> A complete visual reference for every page, component, color, animation, and graphic style in the current UI. Anyone reading this should be able to close their eyes and picture every screen.

---

## Table of Contents
1. [Design Philosophy & Visual Language](#1-design-philosophy--visual-language)
2. [Global Color System](#2-global-color-system)
3. [Typography](#3-typography)
4. [Button System](#4-button-system)
5. [Input System](#5-input-system)
6. [Global Background & Decorative Layer](#6-global-background--decorative-layer)
7. [Navigation & Shell](#7-navigation--shell)
8. [Page-by-Page Breakdown](#8-page-by-page-breakdown)
   - [Login Page](#81-login-page)
   - [Onboarding Flow](#82-onboarding-flow)
   - [Home Shell](#83-home-shell)
   - [Moments Page (Daily Deck)](#84-moments-page-daily-deck)
   - [Pending Page (Saved Cards)](#85-pending-page-saved-cards)
   - [Chats List Page](#86-chats-list-page-active-balloons)
   - [Chat Thread Page](#87-chat-thread-page)
   - [Match Profile Preview](#88-match-profile-preview)
9. [Shared Components](#9-shared-components)
10. [Animation Catalogue](#10-animation-catalogue)
11. [Responsive Breakpoints](#11-responsive-breakpoints)
12. [Visual Effects Glossary](#12-visual-effects-glossary)

---

## 1. Design Philosophy & Visual Language

Woven is a dating app built around warmth, intimacy, and intentional connection. Its visual identity reflects this through:

- **Warm, peachy-pink palette** — not cold tech blue; feels like candlelight
- **Soft glassmorphism** — semi-transparent white panels with backdrop blur give the UI depth without feeling heavy
- **Layered backgrounds** — the page is never a flat color; it's a multi-depth system of gradients, decorative floaters, and parallax aura effects
- **Rounded everything** — border-radius is large (14–26px on cards, 999px on chips), creating a friendly, non-corporate feel
- **Motion with intention** — animations are not decorative noise; they signal transitions, reinforce metaphors (balloons float, hearts drift), and communicate state (timer pulses, urgent shake)
- **Minimal chrome** — no heavy navbars or sidebars; content takes up nearly full width
- **Emoji as communication** — emojis (🎈 💗 💥 🎮) are used as lightweight iconography throughout

The overall feel is: **warm minimalism with playful animation accents**.

---

## 2. Global Color System

All colors are defined as CSS custom properties on `:root` in `styles.scss`.

### Core Palette

| Token | Value | Purpose |
|-------|-------|---------|
| `--woven-text` | `#161616` | Primary text, headlines |
| `--woven-text-soft` | `rgba(22, 22, 22, 0.78)` | Body copy, labels |
| `--woven-text-muted` | `rgba(22, 22, 22, 0.55)` | Timestamps, subtitles, placeholders |
| `--accent` | `#ff3b88` | Hot pink — CTAs, highlights, primary buttons |
| `--accent-2` | `#ffb3cf` | Light pink — gradient end, soft accents |
| `--base-1` | `#ffe8dc` | Warm peach — gradient start |
| `--base-2` | `#fef1eb` | Softer beige — gradient middle |
| `--base-3` | `#faf8f6` | Near-white — gradient end |

### State Colors

| Purpose | Color |
|---------|-------|
| Success / Rating Green | `#22c55e` |
| Error / Rating Red | `#ef4444` |
| Danger (destructive) | `#b00020` |
| Trial Timer Gold | `#fbbf24` → `#f59e0b` → `#d97706` |
| Trial Urgent Red | `#ef4444` → `#dc2626` → `#b91c1c` |
| Warning Orange (missing data) | `rgba(255, 170, 0, 0.95)` |
| Game State Yellow | `#FFC107` |
| Game State Green | `#4CAF50` |
| Game State Blue | `#2196F3` |

### Surface Colors (Glass Layers)

| Surface | Background | Border |
|---------|-----------|--------|
| Main panel card | `rgba(255, 255, 255, 0.92)` | `rgba(0, 0, 0, 0.08)` |
| Buttons (default) | `rgba(255, 255, 255, 0.78)` | `rgba(0, 0, 0, 0.10)` |
| Inputs | `rgba(255, 255, 255, 0.82)` | `1.5px solid rgba(0, 0, 0, 0.10)` |
| Bottom tab bar | `rgba(255, 255, 255, 0.78)` | `rgba(0, 0, 0, 0.08)` |
| Message (theirs) | `rgba(255, 255, 255, 0.92)` | `rgba(0, 0, 0, 0.08)` |
| Message (mine) | `rgba(0, 0, 0, 0.04)` | `rgba(0, 0, 0, 0.08)` |
| Chip / tag | `rgba(0, 0, 0, 0.03)` | `rgba(0, 0, 0, 0.10)` |
| Notice box | `rgba(0, 0, 0, 0.03)` | `rgba(0, 0, 0, 0.08)` |

---

## 3. Typography

### Font Families
- **UI font:** `Inter` — weights 400, 500, 600, 700, 800 loaded from Google Fonts
- **Luxury / signature accent:** `Great Vibes` (cursive) — used sparingly for logo or signature moments

### Scale

| Role | Size | Weight | Details |
|------|------|--------|---------|
| Page title | varies | 800 | Large, bold, dark |
| Section kicker | 10–11px | 900–950 | ALL CAPS, 0.18–0.24em letter-spacing |
| Headline / card name | 22–32px | 750–950 | Tight tracking (-0.01em) |
| Body text | 13–15px | 400 | Line-height 1.55 |
| Label / meta | 12px | 600–700 | 65–70% opacity |
| Timestamp | 11px | 400 | 55% opacity, monospaced numbers |
| Tag / chip label | 10–13px | 900 | Uppercase or sentence case |
| Timer countdown | 32px | 700 | `font-variant-numeric: tabular-nums` |

### Base Settings
- Root font: `16px` (desktop) → `15px` (tablet ≤768px) → `14px` (mobile ≤480px)
- Line-height: `1.55` (relaxed)
- Letter-spacing: `-0.01em` (slightly tight, modern feel)

---

## 4. Button System

All buttons share a base class `.btn` which provides the glassmorphic foundation.

### Default / Ghost Button
```
min-height: 46px
padding: 12px 16px
border-radius: 16px
background: rgba(255, 255, 255, 0.78)
border: 1px solid rgba(0, 0, 0, 0.10)
box-shadow: 0 2px 10px rgba(0, 0, 0, 0.05)
backdrop-filter: blur(10px)
font-weight: 650
color: #161616

Hover:
  transform: translateY(-1px)
  box-shadow: 0 14px 32px rgba(0, 0, 0, 0.10)
  border-color: rgba(255, 59, 136, 0.22)  ← subtle pink hint
```

### Primary / CTA Button
```
background: linear-gradient(135deg, #ff3b88, #ffb3cf)
color: white
box-shadow: 0 16px 40px rgba(255, 59, 136, 0.22)  ← pink glow

Hover: same translateY + stronger glow
```

### Black / Dark Button (Onboarding confirm)
```
background: #111
color: white
border-radius: 14px
```

### Chip Button (Pill)
```
border-radius: 999px
min-height: 44px
padding: 10px 14px
background: rgba(0, 0, 0, 0.03)
font-size: 13px
font-weight: 900
```

### Action Buttons (Moments deck — pass/hold/yes)
```
min-height: 80px
border-radius: 16px
background: rgba(255, 255, 255, 0.78)
Emoji: 22px
Label: 14px, weight 950
Sub-label: 12px, 55% opacity
```

### Send Button (Chat composer)
```
background: rgba(0, 0, 0, 0.06)
border-radius: 12px
Hover: translateY(-1px)
```

### Danger / Destructive Button
```
background: rgba(255, 255, 255, 0.75)
border: red-tinted
color: error red (#b00020)
```

---

## 5. Input System

All text inputs share this style:

```
min-height: 46px
padding: 12px 14px
border-radius: 14px
border: 1.5px solid rgba(0, 0, 0, 0.10)
background: rgba(255, 255, 255, 0.82)
backdrop-filter: blur(10px)
font: inherit, 0.98rem
color: #161616

Focus:
  border-color: rgba(255, 59, 136, 0.40)
  outline: none

Placeholder:
  color: rgba(0, 0, 0, 0.55)
```

### Textarea (Foundational Q, Bio)
```
min-height: 120px
border-radius: 16px
border: rgba(0, 0, 0, 0.15)
resize: vertical
```

### Character counter
```
font-size: 12px
opacity: 0.55
float/align: right below textarea
Shows: "12 / 200" or "minimum 30 chars required"
```

### Chat Composer Input
```
width: 100%
min-height: 44px
border-radius: 16px
border: 1px solid rgba(0, 0, 0, 0.10)
background: rgba(255, 255, 255, 0.85)
```

---

## 6. Global Background & Decorative Layer

The background is a three-layer system applied at the HTML/body level — it exists beneath every page.

### Layer 1 — Page Gradient (html element)
```
background: linear-gradient(165deg,
  #ffe8dc,         ← warm peach (top-left)
  #fef1eb 52%,     ← soft beige (center)
  #faf8f6          ← near-white (bottom-right)
)
position: fixed (doesn't scroll with content)
```

### Layer 2 — Parallax Aura (html::before)
A soft glow cloud that subtly shifts as the user moves the mouse.

```
Driven by CSS variables --wx, --wy (set via JavaScript on mousemove)
Multiple radial gradients stacked:
  - rgba(255, 85, 160, 0.14) at top-center
  - rgba(255, 198, 166, 0.16) at bottom-left
  - rgba(255, 255, 255, 0.10) at center
Mix-blend-mode: normal
Transition: 0.3s ease (smooth mouse tracking)
```
Effect: The background feels alive and slightly reactive to the user's presence.

### Layer 3 — Floating Decorative Elements (body::before)
Large emoji-based hearts and stars rendered as CSS `content`, floating in the background behind all content.

```
Elements:
  Large heart: 140px, opacity 0.65, filter: drop-shadow(pink glow)
  Medium heart: 100px, opacity 0.55
  Small heart A: 80px, opacity 0.50
  Small heart B: 80px, opacity 0.45
  Star A: 180px, opacity 0.55
  Star B: varies

Animation: wovenFloat
  Duration: 16s, infinite, ease-in-out
  Motion: translateY from 0 to +12px and back (gentle vertical drift)

Scroll-reactive opacity:
  opacity: calc(0.35 + (var(--scroll) * 0.25))
  → More visible as user scrolls down

Filter on all elements:
  drop-shadow(0 0 15px rgba(255, 59, 136, 0.25))
  drop-shadow(0 0 8px rgba(255, 182, 207, 0.20))
```
The hearts and stars are always behind the main panel, giving the app a dreamy, romantic atmosphere even on plain screens.

---

## 7. Navigation & Shell

### Top Header (Home pages)
```
Position: fixed, top: 20px, z-index: 5
Contains: app logo / name on left, floating pulse pill on right
No background on the header itself — it floats above the page gradient
```

### Floating Pulse Pill (top-right indicator)
```
Position: fixed, right: 22px, top: 74px
Background: rgba(255, 255, 255, 0.85) + blur(10px)
Border: 1px solid rgba(0, 0, 0, 0.10)
Box-shadow: 0 14px 34px rgba(0, 0, 0, 0.10)
Border-radius: 999px
Contains: colored dot + text label

Dot colors:
  Green: active / good status
  Orange rgba(255, 170, 0, 0.95): missing info / needs attention
```

### Bottom Tab Navigation
```
Position: fixed, bottom: ~20px, centered
Width: matches content panel (760px max)
Background: rgba(255, 255, 255, 0.78) + blur(10px)
Border: 1px solid rgba(0, 0, 0, 0.08)
Border-radius: 18px
Box-shadow: 0 18px 40px rgba(0, 0, 0, 0.10)
Padding: 10px
Layout: CSS grid, 3 equal columns, gap: 10px

Tab states:
  Default: opacity 0.62
  Hover: opacity 0.85
  Active: opacity 1.0, background rgba(0, 0, 0, 0.06), border-radius matches pill

Tabs:
  1. Moments (🌟 or similar)
  2. Chats / Balloons (🎈)
  3. Profile (👤)
```

### Main Content Panel
```
Max-width: 760px
Margin: auto (centered)
Min-height: calc(100vh - 170px)
Background: rgba(255, 255, 255, 0.92)
Border: 1px solid rgba(0, 0, 0, 0.08)
Border-radius: 20px (18px on mobile)
Box-shadow: 0 16px 40px rgba(0, 0, 0, 0.08)
Padding: 16px
```

### Watermark
```
Character: "W"
Font-size: 240px (190px on mobile)
Rotation: -8 degrees
Opacity: 0.035
Position: absolute, behind content, decorative only
```

---

## 8. Page-by-Page Breakdown

### 8.1 Login Page

**What you see:** A full-screen animated intro that plays for ~6 seconds, then the login card pops in.

#### Intro Animation Sequence (0–6.2s)

The login screen opens with a theatrical reveal:

1. **0s – 1.2s:** A large stylized "W" logo fades in from a blurred state. Smooth, dramatic.
2. **1.5s – 2.5s:** The W pulses (heartbeat animation, 2 beats). It feels alive.
3. **2.5s – 4s:** The W morphs and transforms into a pink balloon. The background gradient shifts to balloon colors: `linear-gradient(135deg, #ff9a9e, #fecfef 50%, #ffdde1)`. A soft shadow appears.
4. **4s – 6.5s:** The balloon floats upward and shrinks, as if flying away into the sky.
5. **Simultaneously:** 6 tiny hearts drift upward from the bottom, each on staggered animation delays. They fade in, rise, and fade out. Colors: `#ffc1cc`, `#ffd4e5`, `#ffcce5`, `#ffb3d9`.
6. **Also simultaneously:** 4 more balloons float up spelling "OVEN" (to complete "WOVEN" after the W disappears), with gentle rotation wobbles.
7. **6.2s:** The intro overlay fades out (0.8s fade). The login card pops in.

#### Login Card
```
Width: 440px, centered on screen
Background: rgba(255, 255, 255, 0.70)
Border: 1px solid rgba(255, 255, 255, 0.40)
Border-radius: 20px
Backdrop-filter: blur(10px)
Box-shadow: layered soft shadows
Padding: ~28px

Animation: cardPop
  0.6s ease-out
  Scale: 0.9 → 1.05 → 1.0
  Triggers at 6.2s (as intro fades)
```

#### Card Content
- **Title:** "Woven" in large type, possibly with Great Vibes font
- **Subtitle:** Short tagline
- **Google Sign-In button:** Styled as a pill button with Google branding
- **Footer note:** Small muted text

---

### 8.2 Onboarding Flow

The onboarding is a **multi-step wizard** with 8 steps. Each step uses the same outer shell (`woven-onboarding-shell`) and swaps the inner content.

#### Shell Layout
- **Progress pills** across the top: small rounded pills showing version, step count (e.g. "2/5"), and a skip link
  - Pills: `rgba(255, 255, 255, 0.60)`, border-radius: 999px, 12px padding
- **Main content area:** Centered, generous padding, no card border on the shell itself — it sits on the page gradient
- **Navigation buttons:** Back and Next/Finish at the bottom

#### Step Styles

**Welcome Step**
- Large friendly heading, minimal content
- Single CTA button (wide, primary gradient)

**Basics Step** (age, gender, location, preferences)
- Multiple input fields stacked vertically
- Each field: same input style as global system
- Dropdowns match input style

**Intent Step** (relationship goals)
- Question above large textarea or radio-style chips
- Openness tags shown as selectable pill chips
  - Selected: border + accent color highlight
  - Unselected: `rgba(0,0,0,0.03)` background

**Foundational Step** (5 deep questions)
- Kicker label above each question (e.g. "Question 3 of 5")
- Large question text at ~18–20px, weight 700
- Textarea below: 120px min-height, same glass input style
- Character counter (bottom-right of textarea): "47 / 200"
- Min characters message: "minimum 30 characters required" in muted color
- Privacy notice box at bottom:
  ```
  Background: rgba(0, 0, 0, 0.03)
  Border: 1px solid rgba(0, 0, 0, 0.08)
  Border-radius: 12px
  Padding: 10px 12px
  Font: 12px, muted color
  Icon: 🔒 or similar
  ```

**Photos Step**
- Upload frame: dashed border (2px dashed), 340px min-height, centered icon + "Add photo" text
- After upload: image preview at 4:5 aspect ratio with crop
- 3-column thumbnail grid below: each thumbnail 80px square with a numbered badge overlay
- Badge: dark circle with white number (sort order)
- Caption input below each photo: 12px label, input field, character count below
- Buttons: Primary dark (save), Ghost (cancel), Danger (remove)

**Details Step** (bio, optional fields)
- Bio textarea (multi-line, 180px min-height)
- Optional fields as expandable sections or simple inputs
- Weekly vibe: single line input

**Review Step**
- Read-only profile preview card
- Photo grid, all text fields shown
- Edit links next to each section

---

### 8.3 Home Shell

The home layout is the persistent wrapper around Moments, Chats, and Profile pages.

```
Fixed header (floating, not solid bar)
  └── Logo (left)
  └── Floating pulse pill (right)

Main content area (centered, 760px max)
  └── Page-specific content

Fixed bottom tab navigation
  └── 3 tabs: Moments | Chats | Profile

Background: Global 3-layer system always visible
```

The "W" watermark sits behind the main content panel, barely visible at 3.5% opacity.

---

### 8.4 Moments Page (Daily Deck)

**What you see:** A horizontally-scrolling deck of up to 5 candidate cards, each taking the full panel width. Below the deck: 3 action buttons.

#### Page Header
```
Kicker: "TODAY'S DECK" — 11px, weight 950, uppercase, 0.18em spacing, 55% opacity
Title: "Who's calling?" or similar — large, weight 800
Subtitle: muted text, 13px
Theme question: slightly larger, italic or styled differently
```

#### Coach Box (collapsed by default)
```
A collapsible explanation card
Toggle arrow rotates on open/close
Content: 14px body text explaining how the deck works
Background: rgba(255,255,255,0.90)
Border-radius: 16px
Smooth expand/collapse animation
```

#### Hero Card (each candidate)
```
Width: ~100% of panel
Height: clamp(360px, 52dvh, 520px)  ← fluid, feels right on any screen
Border-radius: 26px
Border: 1px solid rgba(0, 0, 0, 0.10)
Box-shadow: 0 24px 90px rgba(0, 0, 0, 0.14)
overflow: hidden
```

**Inside the hero card:**

**Photo Stage (top ~60% of card)**
```
Background: candidate's photo, object-fit: cover
Relative positioning for overlays
```

**Rating Bar Overlay (top-right corner)**
```
Background: rgba(0, 0, 0, 0.5) + blur(8px)
Border-radius: 10px
Padding: 6px 8px
Layout: 4 red bars | vertical divider | 4 green bars
Bar: 7px wide × 16px tall, border-radius: 2px
Red filled: #ef4444
Green filled: #22c55e
Unfilled bars: rgba(255,255,255,0.30)
```

**New User Badge (if applicable)**
```
Same position as rating bar
Contains "NEW" text + pulsing green dot
Dot animation: pulse-dot 2s infinite (scale 0.6→1, opacity 0.6→1)
```

**Name Tag Chip (bottom-left of photo stage)**
```
Position: absolute, bottom: 14px, left: 14px
Background: rgba(0, 0, 0, 0.36)
Border: 1px solid rgba(255,255,255,0.18)
Border-radius: 16px
Padding: 6px 12px
Color: white
Font: 22px name, weight 750
Box-shadow: 0 16px 42px rgba(0, 0, 0, 0.22)
```

**"Why We Think You'll Vibe" Section (bottom ~40% of card)**
```
Background: rgba(255,255,255,0.94)
Border-top: 1px solid rgba(0, 0, 0, 0.08)
Padding: 12px 14px

  Kicker: "WHY WE THINK YOU'LL VIBE"
    font: 10px, weight 950, uppercase, 0.24em spacing

  Headline: match headline text
    font: 13px, weight 900, -0.01em spacing

  Bullets: 2 bullet points
    font: 12px, opacity 75%, line-height 1.3
    Each preceded by a bullet dot
```

#### Deck Scroll Container
```
scroll-snap-type: x mandatory
overflow-x: scroll
scrollbar: hidden
-webkit-overflow-scrolling: touch
Each card snap-aligns to full width
```

#### Actions Bar (below each card)
```
Layout: 3-column CSS grid
Border-top: 1px solid rgba(0, 0, 0, 0.10)
Background: rgba(255, 255, 255, 0.96)
Padding: 14px

Column 1 — Pass button:
  Emoji: ✕ or 😐 (22px)
  Label: "Pass" (14px, weight 950)
  Sub-label: "Not for me" (12px, 55% opacity)
  Background: rgba(255,255,255,0.78)
  Hover: translateY(-1px)

Column 2 — Hold button:
  Emoji: ⏸ or 🕰 (22px)
  Label: "Save" (14px, weight 950)
  Sub-label: "Revisit later" (12px, 55% opacity)
  Background: rgba(0, 0, 0, 0.04)

Column 3 — Yes button:
  Emoji: 💗 (22px)
  Label: "Yes!" (14px, weight 950)
  Sub-label: "I'm interested" (12px, 55% opacity)
  Background: rgba(255, 255, 255, 0.78)
  Hover: translateY(-1px)
  Active press: scale down slightly
```

---

### 8.5 Pending Page (Saved Cards)

Visually almost identical to the Moments page, with minor differences:

- Same hero card style, photo stage, name tag chip
- **Saved-at timestamp overlay** on the photo (top-left or bottom-right), small pill: "Saved 2 days ago"
- Actions bar has only **2 buttons**: "No" (left) and "Yes!" (right)
- Empty state: centered illustration/emoji + heading "Nothing saved yet" + subtext

---

### 8.6 Chats List Page ("Active Balloons")

**What you see:** A vertical list of open matches (balloons), each as a rounded card row.

#### Page Header
```
Kicker: "CHATS" — uppercase, muted, 11px
Title: "Active balloons" — large, weight 800
Subtitle: description text
Meta line: "3 balloons · tap to open" — 12px, muted
```

#### Chat Row Card
```
Background: rgba(255, 255, 255, 0.92)
Border: 1px solid rgba(0, 0, 0, 0.08)
Border-radius: 20px
Padding: 12px
Margin-bottom: 10px
Display: flex, space-between, align-items center

Hover:
  transform: translateY(-1px)
  box-shadow: 0 16px 60px rgba(0, 0, 0, 0.10)
  transition: 0.2s ease
```

**Left section: Avatar**
```
Size: 62×62px
Border-radius: 18px
Border: 1px solid rgba(0, 0, 0, 0.08)
Image: object-fit: cover

Fallback (no photo):
  Background: warm gradient
  Initials: 22px, weight 900, white
```

**Middle section: Info**
```
Name: 15px, weight 950, single line, text-overflow: ellipsis
Tag pill: 10px, weight 900, background rgba(0,0,0,0.04), border-radius 999px, padding 2px 8px
Mini text (match type info): 12px, 65% opacity
Preview text: 12px, 70% opacity, inside a light bg box (rgba(0,0,0,0.03), border-radius 8px)
```

**USP Row (Features row below info)**
A horizontal row of chip buttons showing match features:

```
Chips layout: flex row, gap 8px
Chip style:
  Min-height: 44px
  Padding: 10px 14px
  Border-radius: 999px
  Background: rgba(0, 0, 0, 0.03)
  Border: 1px solid rgba(0, 0, 0, 0.10)
  Font: 13px, weight 900, 90% opacity
  Icon (emoji): 14px

States:
  Default: rgba(0,0,0,0.03)
  Ready/active: rgba(0,0,0,0.06) with stronger border
  Find Love ready: rgba(255, 192, 203, 0.12) with border rgba(255,192,203,0.25)
  Danger (pop button): rgba(255,255,255,0.75), red tint
```

**Right section: Open pill**
```
Small rounded pill button
Text: "Open →" or arrow icon
Background: rgba(0, 0, 0, 0.04)
Border: 1px solid rgba(0, 0, 0, 0.12)
Hover: darker background
```

---

### 8.7 Chat Thread Page

**What you see:** A full-screen chat interface with a header, optional status banners, messages list, and composer at the bottom.

#### Header
```
Back button (←) on left
Center: match name + status (active / trial)
Right: Game button (🎮) + Profile button (avatar thumbnail)
Background: white or semi-transparent glass
Border-bottom: 1px solid rgba(0,0,0,0.06)
```

#### Trial Timer Banner (appears during 48h trial)
```
Background: linear-gradient(135deg, #fbbf24, #f59e0b, #d97706)  ← gold gradient
Border-radius: 16px
Padding: 14px 16px
Box-shadow: 0 8px 24px rgba(251, 191, 36, 0.35)  ← golden glow

Contents:
  Left: Clock icon (28px) with timerIconBounce animation (1s, gentle)
  Center: Large countdown timer
    Font: 32px, weight 700, tabular-nums
    Text: "48:00:00"
  Right: small label "trial ends"
  Bottom: thin progress bar (6px height, white gradient, decreases over time)

Animation: timerBannerPulse
  2s, ease-in-out, infinite
  Scale: 1.0 → 1.01 (very subtle breathing)
  Shadow: intensifies slightly

Urgent state (< 30 seconds):
  Gradient switches to red: #ef4444 → #dc2626 → #b91c1c
  Timer text color: white, bolder
  Animation: urgentCountdown (0.5s infinite, scale+opacity pulse)
  Banner: urgentBannerPulse (0.8s, red glow)
```

#### Decision Banner (after trial ends, awaiting user input)
```
Background: linear-gradient(135deg, #fef2f2, #fee2e2)  ← soft red
Border: 2px solid #fca5a5
Border-radius: 16px
Padding: 14px 16px

Contents:
  Icon: 20px, pulsing (decisionIconPulse 1.5s, scale 0.85→1.10)
  Text: "Trial ended — make your decision"
  Button: red gradient (#ef4444 → #dc2626)

Animation: decisionBannerShake
  4s, very subtle horizontal wobble (±2px)
  Creates gentle urgency without being aggressive
```

#### USP Bar (between header and messages)
```
Background: rgba(255, 255, 255, 0.86)
Border: 1px solid rgba(0, 0, 0, 0.08)
Border-radius: 18px
Padding: 10px
Display: flex, space-between

Left side chips:
  "Find Love" status chip — shows readiness (Find Love not yet / Find Love ready)
  Expiry info chip — "expires in 2h" etc.

Right side buttons:
  Pop button (💥) — destructive action button, glows when active (popButtonPulse 2s infinite)
  More menu button (⋯) — opens dropdown
```

#### Messages List
```
Height: calc(100vh - 270px)
Overflow-y: scroll
Scrollbar: custom 4px, rgba(0,0,0,0.15) thumb
Display: flex column
Gap: 10px
Padding: 10px
```

**Message Bubble (theirs)**
```
Align: flex-start
Max-width: 78%, hard cap 520px
Background: rgba(255, 255, 255, 0.92)
Border: 1px solid rgba(0, 0, 0, 0.08)
Border-radius: 18px (with small bottom-left cut: 4px)
Padding: 10px 12px
Box-shadow: 0 10px 30px rgba(0, 0, 0, 0.06)
Font: 13px body text, 90% opacity

Timestamp below bubble:
  11px, 55% opacity, margin-top 4px
  Shows: "2:34 PM"
```

**Message Bubble (mine)**
```
Align: flex-end
Background: rgba(0, 0, 0, 0.04)
Border: 1px solid rgba(0, 0, 0, 0.08)
Border-radius: 18px (with small bottom-right cut: 4px)
Same padding, font, shadow
```

**Message Animations**
```
Appear: messageArrive
  0.3s ease-out
  translateY(10px) → translateY(0), opacity 0 → 1
  Makes each new message slide up into position

Pop (on balloon pop): messageDissolve
  0.4s ease-in
  opacity 1→0, scale 1→0.95, blur(4px)
```

**System Messages** (e.g. "Trial started", "Find Love unlocked")
```
Centered in the message flow
Small pill: 11px, weight 600, background rgba(0,0,0,0.04), border-radius 999px
Soft border, no shadow
Color: muted text
```

#### Composer (Message input bar)
```
Position: sticky, bottom: 0
Background: rgba(255, 255, 255, 0.94)
Backdrop-filter: blur(8px)
Border-top: 1px solid rgba(0, 0, 0, 0.06)
Padding: 10px 12px
Display: flex, gap: 8px

Input:
  Flex: 1
  Min-height: 44px
  Border-radius: 16px
  Background: rgba(255,255,255,0.85)
  Border: 1px solid rgba(0,0,0,0.10)
  Padding: 10px 14px

Send button:
  32×32px
  Background: rgba(0, 0, 0, 0.06)
  Border-radius: 12px
  Icon: →  or paper plane
  Hover: translateY(-1px), slightly darker bg
```

#### Game Picker Dropdown (appears above composer)
```
Trigger: 🎮 button in header
Position: absolute, above composer
Background: rgba(255, 255, 255, 0.98)
Border: 1px solid rgba(0, 0, 0, 0.08)
Border-radius: 16px
Box-shadow: 0 12px 32px rgba(0, 0, 0, 0.12)
Padding: 8px

Animation: slideDown
  0.2s ease-out
  translateY(-8px) → translateY(0), opacity 0 → 1

Each game option:
  Display: flex row
  Icon: 32px emoji or illustration
  Title: 14px, weight 700
  Description: 12px, muted
  Hover: translateY(-2px), stronger shadow
```

#### Find Love Celebration (triggered when both message)
```
Overlay appears:
  Semi-transparent backdrop: rgba(255,255,255,0.60) with blur
  Center card: white, border-radius 20px, padding 24px

Animations running simultaneously:
  gentleGlow (1.5s): the card glows softly
  confettiSoftFall (2.5s): ~12 confetti pieces (small colored squares) fall from top
  findLoveCelebrate (1.5s): card scales up from 0.85 to 1.0
  heartPulse (0.6s): pink heart icon pulses

Date Idea reveal:
  dateIdeaReveal (0.5s): slides up, opacity 0→1
  dateIdeaGlow (1.5s): the idea text glows with pink shadow
```

#### Toast Notifications
```
Position: fixed, bottom-center (above composer)
Background: rgba(22, 22, 22, 0.88)
Color: white
Border-radius: 12px
Padding: 10px 16px
Font: 13px, weight 600
Max-width: 320px

Animation: toastSlideIn
  0.2s ease-out
  translateY(20px) → translateY(0), opacity 0 → 1
Auto-dismiss after 3s
```

---

### 8.8 Match Profile Preview

**What you see:** A scrollable vertical feed of the match's profile. Each "section" is full-height (78vh), scroll-snapped.

#### Feed Container
```
Height: 78vh
Overflow-y: scroll
Scroll-snap-type: y mandatory
Scrollbar: hidden
```

#### Feed Item (each section)
```
Min-height: 78vh
Display: CSS grid, 2 rows: photo stage + info card
Scroll-snap-align: start
```

#### Photo Stage (top ~65% of each item)
```
Position: relative
Overflow: hidden
Contains: photo (object-fit: cover, 100% width/height)

Caption Button Overlay (top-left):
  Position: absolute, top: 12px, left: 12px
  Background: rgba(0, 0, 0, 0.55)
  Border-radius: 20px
  Color: white
  Font: 12px

Caption Popover (appears on tap):
  Position: below button, absolute
  Background: rgba(0, 0, 0, 0.70)
  Color: white
  Border-radius: 12px
  Max-width: 200px
  Font: 12px, line-height 1.4

Hero Tag (bottom-left of photo):
  Name: 22px, weight 750, white
  Age/location: 14px, 80% opacity, white
  Background: gradient from transparent to rgba(0,0,0,0.45)
```

#### Info Card (bottom ~35% of each item)
```
Background: rgba(255, 255, 255, 0.96)
Border-top: 1px solid rgba(0, 0, 0, 0.10)
Padding: 14px 14px 16px

Title (card section label):
  Font: 11px, weight 900, uppercase, 0.18em spacing, 55% opacity
  E.g.: "ABOUT ME", "VALUES", "LIFESTYLE"

Body text:
  Font: 15px, line-height 1.55
  Color: rgba(12, 18, 28, 0.88)  ← slightly warm dark

Chips row (for tag-type data):
  Horizontal scroll, no scrollbar visible
  -webkit-overflow-scrolling: touch
  Chip: 12px, background rgba(0,0,0,0.03), border rgba(0,0,0,0.10)
  Border-radius: 999px, padding: 6px 12px
```

---

## 9. Shared Components

### Game Message Card (in chat thread)

A card that appears inline in the message feed to represent a game session.

```
Max-width: 520px
Background: white (#fff)
Border: 1px solid rgba(0, 0, 0, 0.08)
Border-radius: 16px
Padding: 12px
Box-shadow: 0 6px 18px rgba(0, 0, 0, 0.06)
Transition: all 0.3s ease

Left border (game state):
  Pending:   3px solid #FFC107  ← yellow
  Active:    3px solid #4CAF50  ← green
  Completed: 3px solid #2196F3  ← blue

Top section:
  Badge: 12px, weight 700, background rgba(0,0,0,0.06), border-radius 6px, padding 2px 6px
  Title: weight 700, flex:1
  Meta: 12px, 70% opacity

Body:
  Font: 14px, 90% opacity, line-height 1.4

Action buttons:
  Flex row, gap: 10px
  Each: flex:1, border-radius:12px, border rgba(0,0,0,0.12)

  Primary: background #111, color white
  Ghost: transparent, hover rgba(0,0,0,0.05)
  Both: hover translateY(-2px), shadow 0 4px 12px rgba(0,0,0,0.10)

Waiting state:
  Background: rgba(255, 193, 7, 0.10)  ← yellow tint
  Border-radius: 12px, padding: 10px 12px
  Font: 13px, italic, 70% opacity

Expired state:
  Background: rgba(158, 158, 158, 0.10)
  Text: 50% opacity, italic
```

### Avatar Circle (in messages, headers)
```
Size: 30×30px (small), 62×62px (chat list)
Border-radius: 999px (small) / 18px (list)
Border: 1px solid rgba(0, 0, 0, 0.10)
Fallback: initials on warm gradient
```

---

## 10. Animation Catalogue

### Global / Persistent

| Name | Duration | Effect |
|------|----------|--------|
| `wovenFloat` | 16s infinite | Background hearts drift ±12px vertically |
| `messageArrive` | 0.3s | New message slides up from 10px below |
| `toastSlideIn` | 0.2s | Toast rises from below with fade |
| `slideDown` | 0.2s | Game picker drops with fade |
| `pulse-dot` | 2s infinite | New user badge dot pulses scale+opacity |

### Login Page Animations

| Name | When | Duration | Effect |
|------|------|----------|--------|
| `logoAppear` | 0s | 1.2s | W fades in from blur, scales up |
| `heartbeat` | 1.5s | 1s × 2 | W pulses like a heartbeat |
| `transformToBalloon` | 2.5s | 1.5s | W morphs, acquires balloon gradient + shadow |
| `floatUp` | 4s | 2.5s | Balloon ascends off-screen, shrinks |
| `heartFloat` | staggered | 8s each | Hearts drift from bottom, fade in/out |
| `balloonFloat` | staggered | 7s each | "OVEN" balloons drift up with wobble |
| `stringAppear` | after balloon | 0.5s | String appears below balloon |
| `stringSway` | continuous | 3s infinite | String sways gently |
| `cardPop` | 6.2s | 0.6s | Login card scales 0.9→1.05→1.0 |
| `fadeOut` (intro) | 6.2s | 0.8s | Entire intro overlay fades out |

### Chat Thread Animations

| Name | Duration | Effect |
|------|----------|--------|
| `timerBannerPulse` | 2s infinite | Gold banner breathes (scale 1.0→1.01) |
| `timerIconBounce` | 1s infinite | Clock icon gently bounces |
| `urgentCountdown` | 0.5s infinite | Timer text pulses when < 30s |
| `urgentBannerPulse` | 0.8s infinite | Red glow intensifies rapidly |
| `decisionBannerShake` | 4s | Slow horizontal wobble (±2px) |
| `decisionIconPulse` | 1.5s infinite | Decision icon scales 0.85→1.10 |
| `urgentPulse` | 2s infinite | Chip glows when < 2 min |
| `popButtonPulse` | 2s infinite | Pop button glows |
| `menuSlideIn` | 0.2s | More menu slides down |
| `balloonInflate` | 0.5s | Element enters with scale-up |
| `popFade` | 0.4s | Element exits with scale-down |
| `messageDissolve` | 0.4s | Message fades+blurs on pop |
| `findLoveCelebrate` | 1.5s | Celebration card scales in |
| `heartPulse` | 0.6s | Heart icon scale pulse |
| `gentleGlow` | 1.5s | Card softly glows pink |
| `confettiSoftFall` | 2.5s | ~12 small colored squares fall |
| `dateIdeaReveal` | 0.5s | Date idea card slides up |
| `dateIdeaGlow` | 1.5s | Date idea text gets pink glow |
| `fadeToGray` | 0.4s | Unmatch — content fades gray |
| `blockDarken` | 0.4s | Block — content darkens |

### Easing Functions
- Most hover/lift effects: `ease-out`
- Most entrance animations: `cubic-bezier(0.2, 0.8, 0.2, 1)` (snappy ease-out)
- Banner pulses: `ease-in-out`
- All transitions: 0.2s–0.3s (crisp, not sluggish)

---

## 11. Responsive Breakpoints

| Breakpoint | Width | Changes |
|------------|-------|---------|
| Desktop | > 768px | Full layout, 240px watermark |
| Tablet | ≤ 768px | Font 15px, minor spacing |
| Mobile | ≤ 520px | Watermark 190px, reduced panel radius |
| Small Mobile | ≤ 480px | Font 14px, chat rows flex-start, tighter actions |
| Extra Small | ≤ 380px | Hero card smaller, tightest spacing, smaller action buttons |

Mobile-specific:
- `-webkit-overflow-scrolling: touch` on all carousels
- Scrollbars hidden on all scroll containers
- Hero card height via `clamp()` for fluid sizing
- Deck: horizontal touch scroll with snap
- Profile feed: vertical touch scroll with snap

---

## 12. Visual Effects Glossary

**Glassmorphism** — semi-transparent white backgrounds + `backdrop-filter: blur()`. Used on: buttons, tab bar, pulse pill, rating bar, chat composer. Creates a "frosted glass" look.

**Depth layering** — The UI has visible Z-depth: background gradient → floating hearts (z=0) → content panel (z=1) → watermark (z=0 behind panel) → floating header/pill (z=5) → tab bar (z=5) → modals (z=100+).

**Pink glow** — Primary CTA buttons cast a `box-shadow: 0 16px 40px rgba(255, 59, 136, 0.22)` which is the accent color glow. Creates warmth and draws the eye.

**Lift on hover** — Almost everything interactive lifts `translateY(-1px)` on hover. This universal micro-interaction gives the UI a physically responsive feel.

**Scroll snap** — Both horizontal (moments deck) and vertical (profile feed) use CSS scroll snap for magnetic, intentional navigation.

**Emoji as UI** — Rather than SVG icon libraries, the app uses emoji (🎈 💗 💥 🎮 ✕) as lightweight, expressive iconography. Consistent font rendering is accepted as part of the character.

**Tabular numbers** — The trial timer uses `font-variant-numeric: tabular-nums` so digits don't shift width as the countdown changes.

**Scroll-reactive decorations** — Background hearts/stars increase in opacity as the user scrolls: `opacity: calc(0.35 + (var(--scroll) * 0.25))`. This rewards exploration.

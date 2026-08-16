# Woven Landing Page — 3D Scroll Architecture
**Status:** Design → Implementation  
**Updated:** 2026-07-04

---

## Design Principles

1. **Invisible AI thesis visual** — ECHO works in the background, users see results
2. **Evidence-only claims** — every statement grounded in real product features
3. **Cinematic scroll journey** — parallax, depth, reveal animations
4. **Woven visual language** — dark plum theme, gold/crimson/violet accents, textile weave texture
5. **Performance-first** — 60fps scroll, lazy-loaded assets, optimized Three.js

---

## Tech Stack (Already Installed)

- **Three.js** (`^0.184.0`) — 3D symbols, depth layers, particle systems
- **GSAP** (`^3.15.0`) — scroll-triggered animations, parallax, reveals
- **Motion One** (`^12.39.0`) — spring physics for micro-interactions
- **Lottie** (`^5.13.0`) — icon animations if needed

---

## Scroll Structure (8 Sections)

### **Section 1: Hero**
- **Visual:** 3D "WOVEN" wordmark with crimson thread weaving through violet/gold hands (subtle nod to love story video)
- **Copy:** "Dating apps optimized for *your* outcome, not their engagement metrics."
- **CTA:** "Join Waitlist" (if not live) OR "Start Matching"
- **Animation:** Camera dolly forward on scroll, thread completes weave

---

### **Section 2: The Problem (Swipe Fatigue)**
- **Visual:** Abstract scroll of infinite faces fading to gray, motion blur
- **Copy:**
  - "Most dating app users describe their experience as exhausting."
  - "You're not tired of dating. You're tired of swiping."
  - "The problem isn't a lack of people. The problem is the apps aren't working."
- **Animation:** Infinite scroll effect, faces blur/desaturate on scroll down

---

### **Section 3: The Woven Difference (Intentionality)**
- **Visual:** 3D Moments card rotating into view, ◈ and ◇ symbols glow
- **Copy:**
  - "5 curated profiles per day. Not 500."
  - "Every choice requires a note (20-150 characters). No reflexive swipes."
  - "Behavioral ML that learns what you *do*, not what you *say* you want."
- **Animation:** Card flips, note overlay appears, ECHO pattern visualizes in background

---

### **Section 4: ECHO Engine (Invisible AI)**
- **Visual:** 16-component scoring visualization — abstract constellation connecting nodes
- **Copy:**
  - "16 behavioral signals. 9 embedding types. Per-user weight adaptation."
  - "ECHO learns from: how fast you respond, whether you listen to voice notes, whether you accept trial periods."
  - "You never see a compatibility score. You just see better matches."
- **Animation:** Constellation animates, nodes pulse, connections form/dissolve
- **Label overlay:** Small tooltips on hover: "Voice Resonance (192-dim)", "Pillar Alignment (1536-dim)", "Orbit Gravity"

---

### **Section 5: The Journey (Trial → Find Love)**
- **Visual:** Split-screen chat interface with 3-minute trial countdown, then 3 date idea cards
- **Copy:**
  - "A match creates a Balloon — a 7-day connection window."
  - "After 3 minutes of messaging, both decide: CONTINUE or END."
  - "If both continue, ECHO generates 3 personalized date ideas."
  - "From swipe to real date — built into the product."
- **Animation:** Chat bubbles appear, timer counts down, date cards flip in

---

### **Section 6: Voice + Games (Rich Signals)**
- **Visual:** Waveform visualization of voice note + game card (KnowMe / RedGreenFlag)
- **Copy:**
  - "Voice notes up to 180 seconds. Because tone matters."
  - "AI-powered games (KnowMe, Red/Green Flag) to accelerate depth."
  - "Every interaction feeds ECHO. Nothing is wasted."
- **Animation:** Waveform animates, game cards shuffle

---

### **Section 7: Privacy + Security**
- **Visual:** AES lock icon, blob storage diagram (abstract), no scores UI mockup
- **Copy:**
  - "AES-256-GCM encryption on all PII."
  - "Private Azure Blob storage. No public URLs."
  - "No compatibility scores shown. No community ratings. No AI badges."
  - "Invisible AI = no performance anxiety."
- **Animation:** Lock icon glows, "SCORE: 87%" mockup fades to "What caught our eye" explanation

---

### **Section 8: CTA + Footer**
- **Visual:** 3D textile weave resolves into stable pattern
- **Copy:**
  - "Join thousands of intentional daters."
  - "Download Woven — iOS & Android"
  - CTA buttons (App Store, Google Play)
- **Animation:** Weave stabilizes, buttons glow on hover
- **Footer:** Privacy Policy · Terms · Contact · Built with ❤️ by [team]

---

## Technical Implementation Plan

### **File Structure**
```
frontend/woven-frontend/src/app/pages/landing/
├── landing.component.ts
├── landing.component.html
├── landing.component.scss
└── sections/
    ├── hero-section.component.ts
    ├── problem-section.component.ts
    ├── intentionality-section.component.ts
    ├── echo-section.component.ts
    ├── journey-section.component.ts
    ├── signals-section.component.ts
    ├── privacy-section.component.ts
    └── cta-section.component.ts
```

### **Scroll Control**
- GSAP ScrollTrigger for all section reveals
- Three.js camera position synced to scroll progress
- Parallax layers: background (0.2x), midground (0.5x), foreground (1x)

### **Performance**
- Lazy-load Three.js scenes (IntersectionObserver)
- requestAnimationFrame for scroll sync
- GPU-accelerated transforms (`translate3d`, `transform: translateZ`)
- Debounced resize handlers
- Preload critical assets (WOVEN logo, first section 3D model)

### **Responsive**
- Mobile: vertical scroll, simplified 3D (fewer particles)
- Tablet: same as desktop, adjusted camera FOV
- Desktop: full parallax + 3D depth

---

## Copy Tone
- **Confident, not arrogant:** "We built this because the alternatives don't work."
- **Evidence-based:** Every claim traceable to PRODUCT_STORY.md or DIFFERENTIATION.md
- **User-outcome focused:** "Your outcome" > "our algorithm"
- **No AI hype:** "Invisible AI" = infrastructure, not feature

---

## Visual Language Consistency

All colors from `styles.scss`:
- **Backgrounds:** `--bg-base`, `--bg-surface`, `--bg-elevated`
- **Accents:** `--rose-400` (crimson), `--gold-400`, `--plum-400` (violet)
- **Text:** `--text-primary`, `--text-secondary`, `--text-muted`
- **Glows:** `--shadow-rose`, `--shadow-gold`, `--shadow-plum`

Fonts:
- **Display (headings):** Fraunces
- **UI (body):** DM Sans
- **Data (numbers, stats):** JetBrains Mono

Motion:
- **Easing:** `--ease-spring` for playful, `--ease-out` for smooth
- **Duration:** `--dur-base` (220ms), `--dur-slow` (380ms)

---

## Next Steps
1. ✅ Architecture designed
2. ⏳ Write final marketing copy for all 8 sections
3. ⏳ Build landing page components
4. ⏳ Integrate Three.js scenes
5. ⏳ Wire GSAP ScrollTrigger
6. ⏳ Test performance + responsiveness

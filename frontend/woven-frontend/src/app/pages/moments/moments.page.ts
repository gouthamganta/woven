import {
  Component,
  OnInit,
  OnDestroy,
  ChangeDetectorRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  MomentsService,
  MomentsCard,
  MomentsBudget,
  LikedYouCard,
  HighlightedTile,
} from '../../services/moments.service';
import { ChatService } from '../../services/chat.service';
import { ChatNoteOverlayComponent, OverlayCard } from './chat-note-overlay.component';

type Tab = 'today' | 'liked-you';

@Component({
  selector: 'app-moments-page',
  standalone: true,
  imports: [CommonModule, ChatNoteOverlayComponent],
  templateUrl: './moments.page.html',
  styleUrls: ['./moments.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MomentsPageComponent implements OnInit, OnDestroy {
  activeTab: Tab = 'today';

  // Today tab
  loading = true;
  error = '';
  todayCards: MomentsCard[] = [];
  budget: MomentsBudget | null = null;
  sparkBalance: number | null = null;
  moodLine: string | null = null;
  respondedUserIds = new Set<number>();
  cardShownAt = new Map<number, number>();

  // Liked You tab
  loadingLikedYou = false;
  likedYouCards: LikedYouCard[] = [];
  likedYouLoaded = false;
  likedYouError = '';
  respondedLikedYouIds = new Set<number>();

  // ChatNote overlay
  overlayCard: OverlayCard | null = null;
  overlayChoice: 'MAGICAL' | 'LOGICAL' | null = null;
  overlaySource: 'TODAY' | 'LIKED_YOU' = 'TODAY';
  overlayTimeOnCardMs: number | null = null;

  toast = '';
  deckCompleted = false;

  // Choice flash animation state (userId → 'magical'|'logical'|'pass')
  choosingCard = new Map<number, string>();

  // Cinematic intro state
  cinematicPhoto = new Map<number, number>();   // userId → current photo index
  quoteVisible   = new Set<number>();            // userId → quote overlay visible
  audioPlaying   = new Set<number>();            // userId → audio currently playing
  private cinematicIntervals = new Map<number, ReturnType<typeof setInterval>>();
  private audioElements      = new Map<number, HTMLAudioElement>();

  // Photo gallery (long-press)
  galleryPhotos: string[] = [];
  galleryIndex = 0;
  private longPressTimer?: ReturnType<typeof setTimeout>;

  constructor(
    private moments: MomentsService,
    private chat: ChatService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  async ngOnInit() {
    await this.loadToday();
    this.initCinematic();
  }

  ngOnDestroy() {
    this.cinematicIntervals.forEach(i => clearInterval(i));
    this.audioElements.forEach(a => { a.pause(); a.src = ''; });
    if (this.longPressTimer) clearTimeout(this.longPressTimer);
  }

  // ── Tab switching ──────────────────────────────────────────────────────────

  async switchTab(tab: Tab) {
    this.activeTab = tab;
    this.cdr.markForCheck();
    if (tab === 'liked-you' && !this.likedYouLoaded) {
      await this.loadLikedYou();
    }
  }

  // ── Cinematic intro ───────────────────────────────────────────────────────

  private initCinematic() {
    if (typeof window === 'undefined') return;
    for (const card of this.todayCards) {
      if (card.kenBurnsPhotoUrls?.length) this.startCinematicForCard(card);
    }
  }

  private startCinematicForCard(card: MomentsCard) {
    const userId = card.userId;
    const photos = card.kenBurnsPhotoUrls!;
    if (this.cinematicIntervals.has(userId)) return;

    this.cinematicPhoto.set(userId, 0);
    let idx = 0;

    const tick = () => {
      idx = (idx + 1) % photos.length;
      this.cinematicPhoto.set(userId, idx);
      this.quoteVisible.add(userId);
      this.cdr.markForCheck();
    };
    const interval = setInterval(tick, 2500);
    this.cinematicIntervals.set(userId, interval);

    // Show quote after first photo if only 1 photo
    if (photos.length === 1) {
      setTimeout(() => { this.quoteVisible.add(userId); this.cdr.markForCheck(); }, 2500);
    }

    // Pre-load audio
    if (card.narrationUrl && typeof window !== 'undefined') {
      const audio = new Audio(card.narrationUrl);
      audio.preload = 'auto';
      audio.onended = () => { this.audioPlaying.delete(userId); this.cdr.markForCheck(); };
      this.audioElements.set(userId, audio);
    }
  }

  hasCinematic(card: MomentsCard): boolean {
    return !!(card.kenBurnsPhotoUrls?.length);
  }

  getCinematicPhoto(card: MomentsCard): string {
    if (card.kenBurnsPhotoUrls?.length) {
      const idx = this.cinematicPhoto.get(card.userId) ?? 0;
      return card.kenBurnsPhotoUrls[idx];
    }
    return card.profilePhoto ?? '';
  }

  toggleNarration(card: MomentsCard, ev: MouseEvent) {
    ev.stopPropagation();
    const audio = this.audioElements.get(card.userId);
    if (!audio) return;
    if (audio.paused) {
      audio.play().then(() => { this.audioPlaying.add(card.userId); this.cdr.markForCheck(); }).catch(() => {});
    } else {
      audio.pause();
      this.audioPlaying.delete(card.userId);
      this.cdr.markForCheck();
    }
  }

  // ── Haptic feedback ────────────────────────────────────────────────────────

  private haptic(intensity: 'light' | 'medium' | 'heavy' = 'light') {
    if (typeof navigator === 'undefined' || !('vibrate' in navigator)) return;
    const pattern: Record<string, number | number[]> = {
      light:  10,
      medium: [12, 8, 12],
      heavy:  [30, 10, 30],
    };
    try { navigator.vibrate(pattern[intensity]); } catch { /* ignore */ }
  }

  // ── Photo gallery (long-press) ────────────────────────────────────────────

  onPhotoLongPressStart(card: MomentsCard | LikedYouCard, ev: Event) {
    if (this.longPressTimer) clearTimeout(this.longPressTimer);
    const photos = card.photos?.length ? card.photos : (card.profilePhoto ? [card.profilePhoto] : []);
    if (photos.length === 0) return;
    this.longPressTimer = setTimeout(() => {
      this.galleryPhotos = photos;
      this.galleryIndex  = 0;
      this.cdr.markForCheck();
    }, 500);
  }

  onPhotoLongPressEnd() {
    if (this.longPressTimer) {
      clearTimeout(this.longPressTimer);
      this.longPressTimer = undefined;
    }
  }

  setGalleryIndex(i: number) {
    this.galleryIndex = i;
    this.cdr.markForCheck();
  }

  closeGallery() {
    this.galleryPhotos = [];
    this.galleryIndex  = 0;
    this.cdr.markForCheck();
  }

  // ── Today tab ─────────────────────────────────────────────────────────────

  get visibleTodayCards(): MomentsCard[] {
    return this.todayCards.filter(c => !this.respondedUserIds.has(c.userId));
  }

  async loadToday() {
    this.loading = true;
    this.error = '';
    this.cdr.markForCheck();
    try {
      const data = await firstValueFrom(this.moments.getMoments());
      this.todayCards = data.cards ?? [];
      this.budget = data.budget ?? null;
      this.sparkBalance = data.sparkBalance ?? null;
      this.moodLine = data.moodLine ?? null;
      this.respondedUserIds.clear();
      this.cardShownAt.clear();
      const now = Date.now();
      for (const c of this.todayCards) {
        this.cardShownAt.set(c.userId, now);
      }
    } catch {
      this.error = "Couldn't load today's moments.";
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
      this.initCinematic();
    }
  }

  choose(card: MomentsCard, action: 'magical' | 'logical' | 'pass') {
    if (this.choosingCard.size > 0) return;
    this.haptic(action === 'pass' ? 'light' : 'medium');

    if (action === 'pass') {
      this.choosingCard.set(card.userId, 'pass');
      this.cdr.markForCheck();
      setTimeout(() => { this.choosingCard.delete(card.userId); this.sendPass(card); }, 260);
      return;
    }

    const choice = action === 'magical' ? 'MAGICAL' : 'LOGICAL';
    const shownAt = this.cardShownAt.get(card.userId) ?? Date.now();

    this.choosingCard.set(card.userId, action);
    this.cdr.markForCheck();

    setTimeout(() => {
      this.choosingCard.delete(card.userId);
      this.overlayCard = { userId: card.userId, fullName: card.fullName, profilePhoto: card.profilePhoto, bridgeQuestion: card.reason?.bridgeQuestion ?? null };
      this.overlayChoice = choice;
      this.overlaySource = 'TODAY';
      this.overlayTimeOnCardMs = Date.now() - shownAt;
      this.cdr.markForCheck();
    }, 420);
  }

  private checkDeckCompletion() {
    if (this.visibleTodayCards.length === 0 && this.todayCards.length > 0 && !this.deckCompleted) {
      this.deckCompleted = true;
      this.haptic('heavy');
      this.cdr.markForCheck();
    }
  }

  private async sendPass(card: MomentsCard) {
    this.respondedUserIds.add(card.userId);
    this.checkDeckCompletion();
    this.cdr.markForCheck();
    try {
      const shownAt = this.cardShownAt.get(card.userId) ?? Date.now();
      await firstValueFrom(
        this.moments.respond({
          targetUserId: card.userId,
          choice: 'PASS',
          source: 'TODAY',
          timeOnCardMs: Date.now() - shownAt,
        })
      );
    } catch {
      this.respondedUserIds.delete(card.userId);
      this.showToast('Something went wrong. Try again.');
    } finally {
      this.cdr.markForCheck();
    }
  }

  // ── Liked You tab ─────────────────────────────────────────────────────────

  get visibleLikedYouCards(): LikedYouCard[] {
    return this.likedYouCards.filter(c => !this.respondedLikedYouIds.has(c.userId));
  }

  async loadLikedYou() {
    this.loadingLikedYou = true;
    this.likedYouError = '';
    this.cdr.markForCheck();
    try {
      const data = await firstValueFrom(this.moments.getLikedYou());
      this.likedYouCards = data.cards ?? [];
      this.likedYouLoaded = true;
    } catch {
      this.likedYouError = "Couldn't load who liked you.";
    } finally {
      this.loadingLikedYou = false;
      this.cdr.markForCheck();
    }
  }

  chooseLikedYou(card: LikedYouCard, action: 'magical' | 'logical') {
    if (this.choosingCard.size > 0) return;
    this.haptic('medium');
    const choice = action === 'magical' ? 'MAGICAL' : 'LOGICAL';

    this.choosingCard.set(card.userId, action);
    this.cdr.markForCheck();

    setTimeout(() => {
      this.choosingCard.delete(card.userId);
      this.overlayCard = { userId: card.userId, fullName: card.fullName, profilePhoto: card.profilePhoto, bridgeQuestion: null };
      this.overlayChoice = choice;
      this.overlaySource = 'LIKED_YOU';
      this.overlayTimeOnCardMs = null;
      this.cdr.markForCheck();
    }, 420);
  }

  // ── Overlay callbacks ─────────────────────────────────────────────────────

  onOverlayBack() {
    this.overlayCard = null;
    this.overlayChoice = null;
    this.cdr.markForCheck();
  }

  async onOverlaySubmit(noteText: string) {
    if (!this.overlayCard || !this.overlayChoice) return;

    const { userId } = this.overlayCard;
    const choice = this.overlayChoice;
    const source = this.overlaySource;
    const timeOnCardMs = this.overlayTimeOnCardMs;

    // Optimistically hide and close overlay
    if (source === 'TODAY') {
      this.respondedUserIds.add(userId);
      this.checkDeckCompletion();
    } else {
      this.respondedLikedYouIds.add(userId);
    }
    this.overlayCard = null;
    this.overlayChoice = null;
    this.cdr.markForCheck();

    try {
      const res = await firstValueFrom(
        this.moments.choose({ targetUserId: userId, choice, noteText, source, timeOnCardMs })
      );

      if (res?.sparkBalance != null) this.sparkBalance = res.sparkBalance;

      if (res?.matchId && (res.status === 'PURE_MATCH_CREATED' || res.status === 'EDGE_MATCH_CREATED')) {
        const copy = this.matchRevealCopy(res.matchType ?? '', res.status);
        this.showToast(copy);
        const started = await firstValueFrom(this.chat.start(res.matchId));
        this.router.navigateByUrl(`/chats/${started.threadId}`);
        return;
      }

      if (res.status === 'WAITING_FOR_OTHER_NOTE') {
        this.showToast(choice === 'MAGICAL' ? '◈ Sent — waiting for them' : '◇ Sent — waiting for them');
      } else {
        this.showToast(choice === 'MAGICAL' ? '◈ Sent' : '◇ Sent');
      }
    } catch {
      if (source === 'TODAY') this.respondedUserIds.delete(userId);
      else this.respondedLikedYouIds.delete(userId);
      this.showToast('Something went wrong. Try again.');
    } finally {
      this.cdr.markForCheck();
    }
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  locationLine(c: MomentsCard | LikedYouCard): string {
    const city = (c.location?.city ?? '').toString().trim();
    const state = (c.location?.state ?? '').toString().trim();
    return [city, state].filter(v => !!v).join(', ');
  }

  getRatingBarFill(side: 'red' | 'green', barNumber: number, average: number): boolean {
    if (side === 'red') return average < 0 && average <= -25 * barNumber;
    return average > 0 && average >= 25 * barNumber;
  }

  expiryLabel(hours: number): string {
    if (hours <= 0) return 'Expiring soon';
    if (hours < 24) return `${hours}h left`;
    return `${Math.floor(hours / 24)}d left`;
  }

  private matchRevealCopy(matchType: string, status: string): string {
    const isPure = status === 'PURE_MATCH_CREATED';
    if (isPure && matchType === 'PURE') return 'You both felt it. ◈';
    return 'Different angles, same pull. ◇◈';
  }

  private showToast(msg: string) {
    this.toast = msg;
    this.cdr.markForCheck();
    if (typeof window === 'undefined') return;
    window.setTimeout(() => { this.toast = ''; this.cdr.markForCheck(); }, 2500);
  }
}

import {
  Component,
  Output,
  EventEmitter,
  Input,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface AfterthoughtData {
  rating: number | null;
  reasonTag: string | null;
  noteText: string;
}

export type ClosePathType = 'trial_end' | 'unmatch' | 'block';

interface ReasonOption {
  tag: string;
  label: string;
}

@Component({
  selector: 'woven-afterthought-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './afterthought-modal.component.html',
  styleUrls: ['./afterthought-modal.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AfterthoughtModalComponent {
  @Input() closePath!: ClosePathType;
  @Input() partnerName: string = '';
  @Output() back = new EventEmitter<void>();
  @Output() submitted = new EventEmitter<AfterthoughtData>();

  rating: number | null = null;
  selectedReasonTag: string | null = null;
  noteText = '';

  constructor(private cdr: ChangeDetectorRef) {}

  get title(): string {
    switch (this.closePath) {
      case 'trial_end':
        return 'How was it?';
      case 'unmatch':
        return 'Before you go';
      case 'block':
        return 'Help us keep Woven safe';
      default:
        return 'Your thoughts?';
    }
  }

  get description(): string {
    switch (this.closePath) {
      case 'trial_end':
        return `Your rating helps ECHO learn what works for you. ${this.partnerName} won't see this.`;
      case 'unmatch':
        return `Your rating helps ECHO improve. ${this.partnerName} won't see this.`;
      case 'block':
        return 'This feedback stays private and helps us protect the community.';
      default:
        return 'Private feedback for ECHO.';
    }
  }

  get showRating(): boolean {
    // Block path has no star rating
    return this.closePath !== 'block';
  }

  get reasonOptions(): ReasonOption[] {
    switch (this.closePath) {
      case 'trial_end':
        return [
          { tag: 'no_spark', label: 'No spark' },
          { tag: 'timing', label: 'Wrong timing' },
          { tag: 'not_my_type', label: 'Not my type' },
          { tag: 'conversation', label: 'Conversation felt off' },
          { tag: 'other', label: 'Other' },
        ];
      case 'unmatch':
        return [
          { tag: 'lost_interest', label: 'Lost interest' },
          { tag: 'busy', label: 'Too busy' },
          { tag: 'better_match', label: 'Found a better match' },
          { tag: 'forgot', label: 'Forgot about it' },
          { tag: 'other', label: 'Other' },
        ];
      case 'block':
        return [
          { tag: 'harassment', label: 'Harassment' },
          { tag: 'inappropriate', label: 'Inappropriate content' },
          { tag: 'spam', label: 'Spam or scam' },
          { tag: 'fake_profile', label: 'Fake profile' },
          { tag: 'other_safety', label: 'Other safety concern' },
        ];
      default:
        return [];
    }
  }

  get canSubmit(): boolean {
    // Block path: only needs reason tag (no rating)
    if (this.closePath === 'block') {
      return this.selectedReasonTag !== null;
    }
    // trial_end and unmatch: need both rating and reason
    return this.rating !== null && this.selectedReasonTag !== null;
  }

  get submitLabel(): string {
    return this.closePath === 'block' ? 'Block & Close' : 'Submit';
  }

  selectRating(stars: number) {
    this.rating = stars;
    this.cdr.markForCheck();
  }

  selectReason(tag: string) {
    this.selectedReasonTag = tag;
    this.cdr.markForCheck();
  }

  onBack() {
    this.back.emit();
  }

  onSubmit() {
    if (!this.canSubmit) return;

    this.submitted.emit({
      rating: this.rating,
      reasonTag: this.selectedReasonTag,
      noteText: this.noteText.trim(),
    });
  }
}

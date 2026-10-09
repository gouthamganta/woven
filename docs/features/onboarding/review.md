# Onboarding Review

**Feature:** Profile review before going live  
**Related:** [Onboarding Flow](./flow.md) | [Profile](../profile/profile-page.md)

---

## Overview

After completing onboarding, users can review and edit their profile before it goes live to the matching pool.

---

## Review Steps

**What users see:**
- Name, age, gender, pronouns
- Photos (reorder, delete, add more)
- Foundational question answers
- Location

**Actions:**
- Edit profile fields
- Retake photos
- Revise answers
- Go live → `ProfileStatus = ACTIVE`

---

## Profile Status

**Statuses:**
- `ONBOARDING` — still completing onboarding
- `PENDING_REVIEW` — onboarding done, reviewing before live
- `ACTIVE` — live in matching pool
- `SUSPENDED` — moderation hold

**Evidence:** [ProfileStatus.cs](../../../backend/WovenBackend/data/Entities/ProfileStatus.cs)

---

## Going Live

**Trigger:** User confirms profile → `ProfileStatus = ACTIVE`

**Side effects:**
- Bootstrap vectors generated (if not done)
- User enters candidate pools
- First daily deck generated

**Evidence:** [OnboardingEndpoints.cs](../../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

## Related

- [Onboarding Flow](./flow.md)
- [Profile Page](../profile/profile-page.md)
- [Bootstrap Vectors](./bootstrap-vectors.md)

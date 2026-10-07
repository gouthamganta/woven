# Incident Response

---

## Overview

This document outlines procedures for responding to security incidents at Woven. Incidents include data breaches, unauthorized access, service disruptions, and security vulnerabilities.

**Status:** Initial procedures defined (2026-10-07). Will be refined as team grows.

---

## Incident Classification

### Severity Levels

| Level | Definition | Response Time | Example |
|---|---|---|---|
| **P0 - Critical** | Active breach, data exposed | Immediate | Database credentials leaked |
| **P1 - High** | Potential breach, no confirmed data loss | < 1 hour | Suspicious admin logins |
| **P2 - Medium** | Security vulnerability, not actively exploited | < 4 hours | Unpatched dependency |
| **P3 - Low** | Security concern, low risk | < 24 hours | Weak password on dev account |

---

## Incident Types

### Data Breach

**Definition:** Unauthorized access to user data (PII, chat messages, photos).

**Examples:**
- Database credentials compromised
- S3 bucket made public
- SQL injection exploited
- Insider access abuse

**Response:** See [Data Breach Response](#data-breach-response)

---

### Unauthorized Access

**Definition:** Someone gains access they shouldn't have.

**Examples:**
- Admin account compromised
- JWT signing key leaked
- Azure credentials stolen

**Response:** See [Access Incident Response](#access-incident-response)

---

### Denial of Service

**Definition:** Service unavailable to legitimate users.

**Examples:**
- DDoS attack
- Resource exhaustion
- Database overload

**Response:** See [Service Disruption Response](#service-disruption-response)

---

### Vulnerability Disclosure

**Definition:** Security vulnerability reported or discovered.

**Examples:**
- Researcher reports XSS
- Dependency CVE published
- Code review finds IDOR

**Response:** See [Vulnerability Response](#vulnerability-response)

---

## Response Procedures

### Data Breach Response

**1. Detect (minutes)**
- Alert triggered (Azure Monitor / logs)
- Manual discovery
- User report

**2. Assess (15 minutes)**
- Confirm breach occurred
- Identify affected data
- Determine breach scope (# users, data types)
- Classify severity (P0/P1)

**3. Contain (30 minutes)**
- Revoke compromised credentials
- Rotate affected keys/secrets
- Block attacker IP/fingerprint
- Disable affected endpoints if needed

**4. Eradicate (1-2 hours)**
- Patch vulnerability
- Remove backdoors
- Clean compromised systems

**5. Recover (2-4 hours)**
- Restore from backups if needed
- Verify system integrity
- Re-enable services

**6. Notify (24-72 hours)**
- Affected users (email)
- Regulators if required (GDPR: 72 hours)
- Law enforcement if criminal

**7. Post-Incident**
- Write incident report
- Update runbooks
- Implement preventive measures

---

### Access Incident Response

**Scenario:** Admin account compromised

**Steps:**
1. **Disable account** (Azure portal)
2. **Revoke all sessions** (invalidate JWTs)
3. **Review audit logs** (what did they access?)
4. **Rotate secrets** (if admin had access)
5. **Enable MFA** (if not already)
6. **Notify team**

**Checklist:**
- [ ] Account disabled
- [ ] Sessions revoked
- [ ] Audit log reviewed
- [ ] Secrets rotated
- [ ] MFA enforced
- [ ] Incident report written

---

### Service Disruption Response

**Scenario:** API down due to DDoS

**Steps:**
1. **Confirm DDoS** (traffic spike in Azure Monitor)
2. **Enable Azure Front Door WAF** (if not already)
3. **Rate limit aggressive IPs** (Azure NSG rules)
4. **Scale up Container Apps** (if resource exhaustion)
5. **Communicate status** (status page / Twitter)
6. **Monitor recovery**

**Escalation:**
- If > 30 minutes: Contact Azure support
- If > 1 hour: Notify users
- If > 4 hours: Executive escalation

---

### Vulnerability Response

**Scenario:** XSS vulnerability reported

**Steps:**
1. **Acknowledge report** (within 24 hours)
2. **Reproduce** (confirm it's real)
3. **Assess impact** (CVSS score)
4. **Patch** (fix the vulnerability)
5. **Test** (verify fix works)
6. **Deploy** (push to production)
7. **Notify reporter** (credit if requested)
8. **Publish advisory** (if high severity)

**SLA:**
- P0/P1: Patch within 24 hours
- P2: Patch within 7 days
- P3: Patch within 30 days

---

## Communication

### Internal

**Slack channel:** `#security-incidents` (create when needed)

**Notification template:**
```
🚨 SECURITY INCIDENT 🚨

Severity: P1
Type: Data Breach
Summary: Database credentials leaked on GitHub
Status: Contained
Affected: 0 users (caught before exploitation)
Next steps: Rotating all DB credentials, enabling GitHub secret scanning

Incident lead: @founder
```

---

### External

**User notification template (email):**
```
Subject: Security Notice: Your Woven Account

Hi [Name],

We're writing to inform you of a security incident that may have affected your account.

What happened: [brief description]
What data was affected: [email, name, messages, etc.]
What we've done: [steps taken to contain]
What you should do: [reset password, review activity, etc.]

We take your privacy seriously and apologize for this incident.

If you have questions, reply to this email.

- The Woven Team
```

**Regulatory notification (GDPR):**
- **Who:** Local data protection authority
- **When:** Within 72 hours of detection
- **How:** Via authority's online portal
- **What:** Breach nature, categories of data, approx. # affected, measures taken

---

## Escalation

### Internal Escalation Path

1. **Developer** (discovers issue) → Founder
2. **Founder** (assesses severity) → Azure support (if infra) / Legal (if breach)
3. **Legal** (advises) → Regulators / Users

### External Escalation

**When to involve:**
- **Azure Support:** Infrastructure issues, DDoS, scaling limits
- **Legal:** Data breach (GDPR/CCPA), law enforcement
- **Law Enforcement:** Criminal activity, threats to users
- **Regulators:** Data breach affecting EU/CA residents

---

## Post-Incident

### Incident Report Template

**Incident ID:** `INC-2026-001`  
**Date:** 2026-10-07  
**Severity:** P1  
**Type:** Data Breach

**Summary:**
[What happened in 2-3 sentences]

**Timeline:**
- 12:00 — Incident detected
- 12:15 — Severity assessed (P1)
- 12:30 — Credentials rotated
- 13:00 — Vulnerability patched
- 14:00 — Service restored
- 16:00 — Users notified

**Root Cause:**
[Why it happened]

**Impact:**
- Users affected: 0
- Data exposed: None
- Downtime: 30 minutes

**Response Actions:**
1. Rotated database credentials
2. Enabled GitHub secret scanning
3. Added pre-commit hook for secrets

**Lessons Learned:**
- Never commit secrets to Git
- Enable secret scanning proactively
- Audit logs caught it early (good!)

**Preventive Measures:**
- [ ] Migrate all secrets to Azure Key Vault
- [ ] Enable Dependabot alerts
- [ ] Add security training for team

**Report Author:** Founder  
**Date:** 2026-10-08

---

### Retrospective

**Schedule:** Within 7 days of incident

**Attendees:** All involved parties

**Agenda:**
1. What happened? (facts)
2. What went well? (wins)
3. What went poorly? (gaps)
4. What will we change? (action items)

**Blameless:** Focus on systems, not individuals.

---

## Prevention

### Proactive Measures

1. **Security training** — For all team members
2. **Dependency scanning** — Dependabot + Snyk
3. **Secrets scanning** — GitHub + pre-commit hooks
4. **Code review** — All changes reviewed
5. **Penetration testing** — Annual (when budget allows)
6. **Bug bounty** — When GA (planned)
7. **Incident drills** — Quarterly tabletop exercises

---

### Security Checklist

**Monthly:**
- [ ] Review Azure NSG rules
- [ ] Audit admin accounts
- [ ] Check for unpatched dependencies
- [ ] Review security audit logs

**Quarterly:**
- [ ] Rotate encryption keys
- [ ] Review GDPR compliance
- [ ] Test backup restoration
- [ ] Run incident drill

**Annually:**
- [ ] Penetration test
- [ ] Security training refresh
- [ ] Update incident response plan

---

## Contact Information

**Security Team:**
- **Founder:** [Contact info in internal docs]

**External:**
- **Azure Support:** Azure portal → Support
- **Legal Counsel:** [To be determined]
- **Data Protection Authority (India):** [To be determined]

**Security Researchers:**
- Email: security@wooven.me (create when ready)
- PGP Key: [To be published]

---

## Known Limitations

**Current gaps:**
- No dedicated security team (founder handles)
- No on-call rotation (single point of failure)
- No status page (manual communication)
- No security training program
- No penetration testing yet
- No bug bounty program

**Will address as team/budget grows.**

---

**Last Updated:** 2026-10-07  
**Next Review:** 2027-01-07 (quarterly)  
**Owner:** Founder

# Gamexercise Docs

Static documents hosted at `https://<your-github-username>.github.io/<repo-name>/`.

Currently:
- `privacy-policy.html` — required by Apple for any iOS app using HealthKit. Linked from App Store Connect's privacy section and from the in-app Settings screen.

## Publishing via GitHub Pages

This folder is wired up to serve as a GitHub Pages site. To get the privacy policy live at a public URL:

1. **Create a GitHub repo** (if not already done). Easiest: public repo named `gamexercise` (private repos can also host Pages on a paid plan, but public is fine for a privacy policy — the document is meant to be public anyway).

2. **Push the local repo to GitHub:**
   ```
   git remote add origin git@github.com:<your-username>/<repo-name>.git
   git push -u origin master
   ```

3. **Enable GitHub Pages**: in the repo on github.com:
   - Settings → Pages
   - **Source**: Deploy from a branch
   - **Branch**: `master` (or `main` if renamed)
   - **Folder**: `/docs`
   - Save.

4. **Wait ~1-2 minutes** for GitHub to deploy. The Pages settings page will then display:
   ```
   Your site is live at https://<username>.github.io/<repo>/
   ```
   The privacy policy URL is `https://<username>.github.io/<repo>/privacy-policy.html`.

5. **Verify it loads** on both desktop and mobile Safari before submitting to App Store Connect.

6. **Plug the URL into**:
   - App Store Connect → App Privacy → Privacy Policy URL (required field).
   - In-app Settings panel — give Claude the URL and we'll wire it into the "Privacy Policy" row.

## When to update the privacy policy

Update `privacy-policy.html` AND the "Effective date" at the top whenever:
- A new SDK is added (crash reporting, analytics, ads, etc.) — even if it's been vetted as privacy-respecting.
- The app starts transmitting any data to any server (your own backend, Game Center, iCloud sync, leaderboards, etc.).
- App Store Connect's App Privacy questionnaire answers change.

Apple compares the policy against the questionnaire during review; mismatches cause rejection.

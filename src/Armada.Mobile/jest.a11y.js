// Accessibility audit after every test (setupFilesAfterEnv): whatever a test left rendered must pass
// src/test/a11y.ts (touchables with a role and a name, labelled inputs, switches and images, no controls hidden
// inside another accessibility element, rows that read what they show). Every screen test is an a11y test too.
//
// The hook is registered before React Native Testing Library is loaded, so it runs before RNTL's own
// afterEach cleanup unmounts the tree. The pure entry point is used so loading RNTL here registers no hooks.
afterEach(() => {
  const { screen } = require('@testing-library/react-native/pure');
  const { auditAccessibility, formatIssues } = require('./src/test/a11y');
  let root = null;
  try {
    root = screen.root;
  } catch {
    return; // nothing rendered in this test
  }
  if (!root) return;
  const issues = auditAccessibility(root);
  if (issues.length > 0) throw new Error(`Accessibility issues in what the test rendered:\n${formatIssues(issues)}`);
});

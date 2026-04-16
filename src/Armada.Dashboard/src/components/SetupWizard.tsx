import { useState, useCallback, useEffect } from 'react';

const STORAGE_KEY = 'armada_setup_completed';

export function isSetupComplete(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

export function markSetupComplete(): void {
  try {
    localStorage.setItem(STORAGE_KEY, 'true');
  } catch {
    // storage unavailable
  }
}

interface StepDef {
  title: string;
  tooltip: string;
  highlightPaths: string[];
  content: React.ReactNode;
}

export interface SetupWizardProps {
  onClose: () => void;
  onHighlightChange?: (paths: string[]) => void;
}

const steps: StepDef[] = [
  {
    title: 'Welcome',
    tooltip: 'Introduction to Armada and what it does',
    highlightPaths: ['/dashboard'],
    content: (
      <>
        <h2 className="wizard-step-heading">Welcome to Armada</h2>
        <p className="wizard-text">
          Armada is a multi-agent orchestration system that scales human developers with AI.
          It coordinates AI coding agents &mdash; called <strong>captains</strong> &mdash; to
          work on tasks across your git repositories.
        </p>
        <p className="wizard-text">
          The Admiral server manages everything: dispatching work, monitoring progress, landing
          changes, and coordinating merge queues. You interact with Armada through this
          dashboard, the CLI (<code>armada</code>), or MCP tools from your editor.
        </p>
        <p className="wizard-text">
          This wizard walks you through the key concepts. You can skip it at any time and
          come back later from the Server page.
        </p>
      </>
    ),
  },
  {
    title: 'Create a Vessel',
    tooltip: 'A vessel is a git repository registered with Armada',
    highlightPaths: ['/vessels', '/fleets'],
    content: (
      <>
        <h2 className="wizard-step-heading">Step 1: Create a Vessel</h2>
        <p className="wizard-text">
          A <strong>vessel</strong> is a git repository that Armada manages. It holds the
          codebase that captains will work on.
        </p>
        <p className="wizard-text">
          When you register a vessel, Armada clones the repository and sets up worktrees so
          multiple captains can work on different branches simultaneously without conflicts.
        </p>
        <p className="wizard-text">
          To get started, add at least one vessel by providing its git clone URL and choosing
          which branch to target.
        </p>
      </>
    ),

  },
  {
    title: 'Create a Captain',
    tooltip: 'A captain is an AI coding agent that executes work',
    highlightPaths: ['/captains', '/docks'],
    content: (
      <>
        <h2 className="wizard-step-heading">Step 2: Create a Captain</h2>
        <p className="wizard-text">
          A <strong>captain</strong> is an AI coding agent &mdash; such as Claude Code,
          Codex, or Gemini CLI &mdash; that Armada assigns work to.
        </p>
        <p className="wizard-text">
          Captains are spawned automatically when you dispatch a voyage, or you can create
          them manually and assign them to specific vessels. Each captain gets its own
          isolated git worktree (called a <strong>dock</strong>) so it can work without
          affecting other captains.
        </p>
        <p className="wizard-text">
          Armada monitors each captain's health and will recover or replace stalled agents
          automatically.
        </p>
      </>
    ),

  },
  {
    title: 'Dispatch Work',
    tooltip: 'Send missions to captains via voyages',
    highlightPaths: ['/dispatch', '/voyages'],
    content: (
      <>
        <h2 className="wizard-step-heading">Step 3: Dispatch Work</h2>
        <p className="wizard-text">
          Work in Armada is organized into <strong>missions</strong> (individual tasks) and
          <strong> voyages</strong> (batches of related missions).
        </p>
        <p className="wizard-text">
          Use the <strong>Dispatch</strong> page to send work to a vessel. Describe what you
          want done, select a target vessel, and Armada will create a voyage, assign
          captains, and start executing.
        </p>
        <p className="wizard-text">
          You can dispatch a single mission or a multi-step voyage with parallel missions.
          Armada handles branch creation, agent assignment, and progress tracking.
        </p>
      </>
    ),

  },
  {
    title: 'Monitor Missions',
    tooltip: 'Track progress, review results, and land changes',
    highlightPaths: ['/missions', '/signals', '/events'],
    content: (
      <>
        <h2 className="wizard-step-heading">Step 4: Monitor Missions</h2>
        <p className="wizard-text">
          Once work is dispatched, you can monitor every mission in real time from the
          <strong> Missions</strong> page or the main <strong>Dashboard</strong>.
        </p>
        <p className="wizard-text">
          Each mission shows its current status, the captain working on it, a live log feed,
          and the resulting diff. When a mission completes, Armada can automatically push,
          create a pull request, or merge &mdash; depending on your landing mode settings.
        </p>
        <p className="wizard-text">
          Failed missions can be restarted, and you can cancel in-flight work at any time.
        </p>
      </>
    ),

  },
];

export default function SetupWizard({ onClose, onHighlightChange }: SetupWizardProps) {
  const [current, setCurrent] = useState(0);
  const step = steps[current];

  useEffect(() => {
    onHighlightChange?.(step.highlightPaths);
    return () => onHighlightChange?.([]);
  }, [current, step.highlightPaths, onHighlightChange]);

  const finish = useCallback(() => {
    markSetupComplete();
    onHighlightChange?.([]);
    onClose();
  }, [onClose, onHighlightChange]);

  return (
    <div className="wizard-overlay">
      <div className="wizard-container">
        {/* Table of contents sidebar */}
        <div className="wizard-toc">
          <div className="wizard-toc-title">Setup Guide</div>
          {steps.map((s, i) => (
            <button
              key={i}
              className={`wizard-toc-item${i === current ? ' active' : ''}${i < current ? ' completed' : ''}`}
              onClick={() => setCurrent(i)}
              title={s.tooltip}
            >
              <span className="wizard-toc-number">{i === 0 ? '\u2605' : i}</span>
              <span className="wizard-toc-label">{s.title}</span>
            </button>
          ))}
        </div>

        {/* Main content area */}
        <div className="wizard-body">
          <div className="wizard-content">{step.content}</div>

          <div className="wizard-actions">
            <button className="btn" onClick={finish}>
              Skip Setup
            </button>
            <div className="wizard-actions-right">
              {current > 0 && (
                <button className="btn" onClick={() => setCurrent(current - 1)}>
                  Back
                </button>
              )}
              {current < steps.length - 1 ? (
                <button className="btn btn-primary" onClick={() => setCurrent(current + 1)}>
                  Next
                </button>
              ) : (
                <button className="btn btn-primary" onClick={finish}>
                  Get Started
                </button>
              )}
            </div>
          </div>
        </div>

        {/* Close (X) button */}
        <button className="wizard-close" onClick={finish} title="Close setup wizard">
          &times;
        </button>
      </div>
    </div>
  );
}

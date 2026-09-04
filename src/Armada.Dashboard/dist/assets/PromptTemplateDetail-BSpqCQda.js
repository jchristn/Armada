import{i as e,n as t,r as n,s as r}from"./LocaleContext-JtHbApia.js";import{Kt as i,T as a,Yr as o,lr as s}from"./client-DejNrGIv.js";import{d as c,l,m as u,p as d}from"./index-DyzK8U3F.js";import{t as f}from"./CopyButton-BcB9qZOW.js";import{t as p}from"./PageHeader-Cf30mAOk.js";import{t as m}from"./ErrorModal-B4cw94ts.js";import{t as h}from"./ConfirmDialog-BVCk6Tga.js";import{t as g}from"./ActionMenu-LLrVN3LW.js";import{t as _}from"./JsonViewer-Cmj7QzvJ.js";import{t as v}from"./StatusBadge-BivHKlKE.js";import{c as y}from"./duplicates-CUTBw3rG.js";var b=r(e(),1),x=n(),ee=[{label:`Mission Context`,params:[{name:`{MissionId}`,description:`Mission identifier`},{name:`{MissionTitle}`,description:`Mission title`},{name:`{MissionDescription}`,description:`Full mission description`},{name:`{MissionPersona}`,description:`Persona assigned to this mission`},{name:`{VoyageId}`,description:`Parent voyage identifier`},{name:`{BranchName}`,description:`Git branch for this mission`}]},{label:`Vessel Context`,params:[{name:`{VesselId}`,description:`Vessel identifier`},{name:`{VesselName}`,description:`Vessel display name`},{name:`{DefaultBranch}`,description:`Default branch (e.g. main)`},{name:`{ProjectContext}`,description:`User-supplied project description`},{name:`{StyleGuide}`,description:`User-supplied style guide`},{name:`{ModelContext}`,description:`Agent-accumulated context`},{name:`{FleetId}`,description:`Parent fleet identifier`}]},{label:`Captain Context`,params:[{name:`{CaptainId}`,description:`Captain identifier`},{name:`{CaptainName}`,description:`Captain display name`},{name:`{CaptainInstructions}`,description:`User-supplied captain instructions`}]},{label:`Pipeline Context`,params:[{name:`{PersonaPrompt}`,description:`Resolved persona prompt text`},{name:`{PreviousStageDiff}`,description:`Diff from prior pipeline stage`},{name:`{ExistingClaudeMd}`,description:`Contents of repo's existing CLAUDE.md`}]},{label:`System`,params:[{name:`{Timestamp}`,description:`Current UTC timestamp`}]}],S=[`mission`,`persona`,`structure`,`commit`,`landing`,`agent`];function C(){let{t:e,formatDateTime:n}=t(),{name:r}=u(),C=d(),w=(0,b.useRef)(null),T=!r,[E,D]=(0,b.useState)(null),[O,k]=(0,b.useState)(!0),[A,j]=(0,b.useState)(``),[M,N]=(0,b.useState)(!1),{pushToast:P}=l(),[F,I]=(0,b.useState)(``),[L,R]=(0,b.useState)(`mission`),[z,B]=(0,b.useState)(``),[V,H]=(0,b.useState)(``),[U,W]=(0,b.useState)(!1),[G,K]=(0,b.useState)({open:!1,title:``,data:null}),[q,J]=(0,b.useState)({open:!1,title:``,message:``,onConfirm:()=>{}}),Y=(0,b.useCallback)(async()=>{if(T){D(null),I(``),R(`mission`),B(``),H(``),W(!1),j(``),k(!1);return}if(r)try{k(!0);let e=await i(r);D(e),I(e.name),R(e.category),B(e.content),H(e.description??``),W(!1),j(``)}catch(t){D(null),j(t instanceof Error?t.message:e(`Failed to load prompt template.`))}finally{k(!1)}},[T,r,e]);(0,b.useEffect)(()=>{Y()},[Y]);function X(e){I(e),W(!0)}function Z(e){R(e),W(!0)}function Q(e){B(e),W(!0)}function te(e){H(e),W(!0)}async function ne(){let t=F.trim(),n=L.trim(),i=V.trim();if(T){if(!t){j(e(`Template name is required.`));return}if(!n){j(e(`Template category is required.`));return}if(!z.trim()){j(e(`Template content is required.`));return}try{N(!0);let r=await a({name:t,category:n,content:z,description:i||void 0,active:!0});D(r),I(r.name),R(r.category),B(r.content),H(r.description??``),W(!1),j(``),P(`success`,e(`Template "{{name}}" created.`,{name:r.name})),C(`/prompt-templates/${encodeURIComponent(r.name)}`,{replace:!0})}catch(t){j(t instanceof Error?t.message:e(`Create failed.`))}finally{N(!1)}return}if(!(!r||!E))try{N(!0);let t=await o(r,{content:z,description:i||void 0});D(t),I(t.name),R(t.category),B(t.content),H(t.description??``),W(!1),j(``),P(`success`,e(`Template saved.`))}catch(t){j(t instanceof Error?t.message:e(`Save failed.`))}finally{N(!1)}}async function re(){if(E)try{N(!0);let t=await a(y(E));P(`success`,e(`Template "{{name}}" duplicated.`,{name:t.name})),C(`/prompt-templates/${encodeURIComponent(t.name)}`)}catch(t){j(t instanceof Error?t.message:e(`Duplicate failed.`))}finally{N(!1)}}function $(){!E||!E.isBuiltIn||J({open:!0,title:e(`Reset to Default`),message:e(`Reset "{{name}}" to its built-in default content? Your customizations will be lost.`,{name:E.name}),onConfirm:async()=>{J(e=>({...e,open:!1}));try{let t=await s(E.name);D(t),B(t.content),H(t.description??``),W(!1),P(`success`,e(`Template reset to default.`))}catch{j(e(`Reset failed.`))}}})}function ie(e){let t=w.current;if(!t)return;let n=t.selectionStart,r=t.selectionEnd,i=z.substring(0,n)+e+z.substring(r);B(i),W(!0),requestAnimationFrame(()=>{t.focus(),t.selectionStart=n+e.length,t.selectionEnd=n+e.length})}return O?(0,x.jsx)(`p`,{className:`text-dim`,children:e(`Loading...`)}):!T&&A&&!E?(0,x.jsx)(m,{error:A,onClose:()=>j(``)}):!T&&!E?(0,x.jsx)(`p`,{className:`text-dim`,children:e(`Template not found.`)}):(0,x.jsxs)(`div`,{children:[(0,x.jsx)(p,{breadcrumb:(0,x.jsxs)(x.Fragment,{children:[(0,x.jsx)(c,{to:`/prompt-templates`,children:e(`Prompt Templates`)}),` `,(0,x.jsx)(`span`,{className:`breadcrumb-sep`,children:`>`}),` `,(0,x.jsx)(`span`,{children:T?e(`Create`):F})]}),title:T?e(`Create Prompt Template`):F,actions:(0,x.jsx)(x.Fragment,{children:T?(0,x.jsx)(v,{status:L||`mission`}):(0,x.jsxs)(x.Fragment,{children:[(0,x.jsx)(v,{status:E.category}),E.isBuiltIn&&(0,x.jsx)(v,{status:`Built-in`}),(0,x.jsx)(g,{id:`template-${E.name}`,items:[{label:`Duplicate`,onClick:()=>void re()},{label:`View JSON`,onClick:()=>K({open:!0,title:e(`Template: {{name}}`,{name:E.name}),data:E})},...E.isBuiltIn?[{label:`Reset to Default`,danger:!0,onClick:$}]:[]]})]})})}),(0,x.jsx)(m,{error:A,onClose:()=>j(``)}),(0,x.jsx)(_,{open:G.open,title:G.title,data:G.data,onClose:()=>K({open:!1,title:``,data:null})}),(0,x.jsx)(h,{open:q.open,title:q.title,message:q.message,onConfirm:q.onConfirm,onCancel:()=>J(e=>({...e,open:!1}))}),(0,x.jsx)(`style`,{children:`
        .template-editor-layout {
          display: grid;
          grid-template-columns: 1fr 340px;
          gap: 1.5rem;
          margin-top: 1rem;
        }
        @media (max-width: 900px) {
          .template-editor-layout {
            grid-template-columns: 1fr;
          }
        }
        .template-editor-panel {
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
        }
        .template-editor-textarea {
          width: 100%;
          min-height: 500px;
          font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace;
          font-size: 0.875em;
          line-height: 1.5;
          padding: 12px;
          border: 1px solid var(--border);
          border-radius: 6px;
          background: var(--input-bg);
          color: var(--text);
          resize: vertical;
          tab-size: 2;
        }
        .template-editor-textarea:focus {
          outline: none;
          border-color: var(--accent);
          box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
        }
        .template-description-input {
          width: 100%;
          padding: 8px 12px;
          border: 1px solid var(--border);
          border-radius: 6px;
          background: var(--input-bg);
          color: var(--text);
          font-size: 0.9em;
        }
        .template-description-input:focus {
          outline: none;
          border-color: var(--accent);
          box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
        }
        .template-meta-field {
          display: grid;
          gap: 0.35rem;
        }
        .template-meta-label {
          font-size: 0.85em;
          color: var(--text-dim);
        }
        .template-param-panel {
          border: 1px solid var(--border);
          border-radius: 6px;
          background: var(--bg-card);
          padding: 1rem;
          max-height: 700px;
          overflow-y: auto;
        }
        .template-param-panel h4 {
          margin: 0 0 0.75rem 0;
          font-size: 0.95em;
          color: var(--text-dim);
        }
        .template-param-group {
          margin-bottom: 1rem;
        }
        .template-param-group:last-child {
          margin-bottom: 0;
        }
        .template-param-group-label {
          font-size: 0.8em;
          font-weight: 600;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          color: var(--text-dim);
          margin-bottom: 0.4rem;
          padding-bottom: 0.25rem;
          border-bottom: 1px solid var(--border);
        }
        .template-param-item {
          display: flex;
          align-items: baseline;
          gap: 0.5rem;
          padding: 4px 0;
          cursor: pointer;
          border-radius: 3px;
          transition: background 0.15s;
        }
        .template-param-item:hover {
          background: var(--bg-hover);
        }
        .template-param-name {
          font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace;
          font-size: 0.8em;
          color: var(--accent);
          white-space: nowrap;
          flex-shrink: 0;
        }
        .template-param-desc {
          font-size: 0.78em;
          color: var(--text-dim);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }
        .template-editor-actions {
          display: flex;
          gap: 0.5rem;
          align-items: center;
        }
        .template-char-count {
          font-size: 0.8em;
          color: var(--text-dim);
          margin-left: auto;
        }
        .template-dirty-indicator {
          display: inline-block;
          width: 8px;
          height: 8px;
          border-radius: 50%;
          background: #f0a040;
          margin-left: 0.25rem;
        }
      `}),(0,x.jsx)(`div`,{className:`detail-grid`,children:T?(0,x.jsxs)(x.Fragment,{children:[(0,x.jsxs)(`label`,{className:`detail-field template-meta-field`,children:[(0,x.jsx)(`span`,{className:`detail-label`,children:e(`Name`)}),(0,x.jsx)(`input`,{className:`template-description-input`,value:F,onChange:e=>X(e.target.value),placeholder:e(`mission.rules.custom`)})]}),(0,x.jsxs)(`label`,{className:`detail-field template-meta-field`,children:[(0,x.jsx)(`span`,{className:`detail-label`,children:e(`Category`)}),(0,x.jsx)(`input`,{className:`template-description-input`,list:`prompt-template-category-options`,value:L,onChange:e=>Z(e.target.value),placeholder:e(`mission`)}),(0,x.jsx)(`datalist`,{id:`prompt-template-category-options`,children:S.map(e=>(0,x.jsx)(`option`,{value:e},e))})]}),(0,x.jsxs)(`div`,{className:`detail-field`,children:[(0,x.jsx)(`span`,{className:`detail-label`,children:e(`Type`)}),(0,x.jsx)(`span`,{children:e(`Custom template`)})]})]}):(0,x.jsxs)(x.Fragment,{children:[(0,x.jsxs)(`div`,{className:`detail-field`,children:[(0,x.jsx)(`span`,{className:`detail-label`,children:e(`ID`)}),(0,x.jsxs)(`span`,{className:`id-display`,children:[(0,x.jsx)(`span`,{className:`mono`,children:E.id}),(0,x.jsx)(f,{text:E.id})]})]}),(0,x.jsxs)(`div`,{className:`detail-field`,children:[(0,x.jsx)(`span`,{className:`detail-label`,children:e(`Active`)}),(0,x.jsx)(v,{status:E.active===!1?`Inactive`:`Active`})]}),(0,x.jsxs)(`div`,{className:`detail-field`,children:[(0,x.jsx)(`span`,{className:`detail-label`,children:e(`Created`)}),(0,x.jsx)(`span`,{children:n(E.createdUtc)})]}),(0,x.jsxs)(`div`,{className:`detail-field`,children:[(0,x.jsx)(`span`,{className:`detail-label`,children:e(`Last Updated`)}),(0,x.jsx)(`span`,{children:E.lastUpdateUtc?n(E.lastUpdateUtc):`-`})]})]})}),(0,x.jsxs)(`div`,{className:`template-editor-layout`,children:[(0,x.jsxs)(`div`,{className:`template-editor-panel`,children:[(0,x.jsxs)(`label`,{style:{fontSize:`0.85em`,color:`var(--text-dim)`},children:[e(`Description`),(0,x.jsx)(`input`,{type:`text`,className:`template-description-input`,value:V,onChange:e=>te(e.target.value),placeholder:e(`Template description...`)})]}),(0,x.jsxs)(`div`,{style:{display:`flex`,justifyContent:`space-between`,alignItems:`center`},children:[(0,x.jsxs)(`label`,{style:{fontSize:`0.85em`,color:`var(--text-dim)`,margin:0},children:[e(`Template Content`),U&&(0,x.jsx)(`span`,{className:`template-dirty-indicator`,title:e(`Unsaved changes`)})]}),(0,x.jsxs)(`span`,{className:`template-char-count`,children:[z.length,` `,e(`characters`)]})]}),(0,x.jsx)(`textarea`,{ref:w,className:`template-editor-textarea`,value:z,onChange:e=>Q(e.target.value),rows:30,spellCheck:!1}),(0,x.jsxs)(`div`,{className:`template-editor-actions`,children:[(0,x.jsx)(`button`,{className:`btn btn-primary`,onClick:ne,disabled:M||!U||T&&(!F.trim()||!L.trim()||!z.trim()),children:e(M?`Saving...`:`Save`)}),E?.isBuiltIn&&(0,x.jsx)(`button`,{className:`btn`,onClick:$,disabled:M,children:e(`Reset to Default`)}),(0,x.jsx)(`button`,{className:`btn`,onClick:()=>C(`/prompt-templates`),children:e(`Back`)})]})]}),(0,x.jsxs)(`div`,{className:`template-param-panel`,children:[(0,x.jsx)(`h4`,{children:e(`Parameters`)}),(0,x.jsx)(`p`,{style:{fontSize:`0.78em`,color:`var(--text-dim)`,margin:`0 0 0.75rem 0`},children:e(`Click a parameter to insert it at the cursor position.`)}),ee.map(t=>(0,x.jsxs)(`div`,{className:`template-param-group`,children:[(0,x.jsx)(`div`,{className:`template-param-group-label`,children:e(t.label)}),t.params.map(t=>(0,x.jsxs)(`div`,{className:`template-param-item`,onClick:()=>ie(t.name),title:e(`Insert {{name}} -- {{description}}`,{name:t.name,description:e(t.description)}),children:[(0,x.jsx)(`span`,{className:`template-param-name`,children:t.name}),(0,x.jsx)(`span`,{className:`template-param-desc`,children:e(t.description)})]},t.name))]},t.label))]})]})]})}export{C as default};
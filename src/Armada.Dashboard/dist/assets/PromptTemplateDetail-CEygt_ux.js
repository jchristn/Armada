import{i as e,n as t,r as n,s as r}from"./LocaleContext-JtHbApia.js";import{$t as i,D as a,Tr as o,mi as ee}from"./client-DgbxOwHa.js";import{n as s}from"./AuthContext-D2cwCavZ.js";import{d as te,l as c,m as ne,p as l}from"./index-DJoUujpC.js";import{t as u}from"./CopyButton-BcB9qZOW.js";import{t as d}from"./PageHeader-Cf30mAOk.js";import{t as f}from"./ErrorModal-B4cw94ts.js";import{t as re}from"./ConfirmDialog-BVCk6Tga.js";import{t as p}from"./ActionMenu-DTXFDXyo.js";import{t as m}from"./JsonViewer-Cmj7QzvJ.js";import{t as h}from"./StatusBadge-BivHKlKE.js";import{c as g}from"./duplicates-CUTBw3rG.js";import{n as _}from"./scoping-CZkYCmjW.js";var v=r(e(),1),y=n(),ie=[{label:`Mission Context`,params:[{name:`{MissionId}`,description:`Mission identifier`},{name:`{MissionTitle}`,description:`Mission title`},{name:`{MissionDescription}`,description:`Full mission description`},{name:`{MissionPersona}`,description:`Persona assigned to this mission`},{name:`{VoyageId}`,description:`Parent voyage identifier`},{name:`{BranchName}`,description:`Git branch for this mission`}]},{label:`Vessel Context`,params:[{name:`{VesselId}`,description:`Vessel identifier`},{name:`{VesselName}`,description:`Vessel display name`},{name:`{DefaultBranch}`,description:`Default branch (e.g. main)`},{name:`{ProjectContext}`,description:`User-supplied project description`},{name:`{StyleGuide}`,description:`User-supplied style guide`},{name:`{ModelContext}`,description:`Agent-accumulated context`},{name:`{FleetId}`,description:`Parent fleet identifier`}]},{label:`Captain Context`,params:[{name:`{CaptainId}`,description:`Captain identifier`},{name:`{CaptainName}`,description:`Captain display name`},{name:`{CaptainInstructions}`,description:`User-supplied captain instructions`}]},{label:`Pipeline Context`,params:[{name:`{PersonaPrompt}`,description:`Resolved persona prompt text`},{name:`{PreviousStageDiff}`,description:`Diff from prior pipeline stage`},{name:`{ExistingClaudeMd}`,description:`Contents of repo's existing CLAUDE.md`}]},{label:`System`,params:[{name:`{Timestamp}`,description:`Current UTC timestamp`}]}],ae=[`mission`,`persona`,`structure`,`commit`,`landing`,`agent`];function b(){let{t:e,formatDateTime:n}=t(),{isAdmin:r,isTenantAdmin:b,user:x}=s(),S={isAdmin:r,isTenantAdmin:b,tenantId:x?.user?.tenantId,userId:x?.user?.id},{name:C}=ne(),w=l(),T=(0,v.useRef)(null),E=!C,[D,O]=(0,v.useState)(null),k=E?!0:!D||_(S,D),[A,j]=(0,v.useState)(!0),[M,N]=(0,v.useState)(``),[P,F]=(0,v.useState)(!1),{pushToast:I}=c(),[L,R]=(0,v.useState)(``),[z,B]=(0,v.useState)(`mission`),[V,H]=(0,v.useState)(``),[U,W]=(0,v.useState)(``),[G,K]=(0,v.useState)(!1),[q,J]=(0,v.useState)({open:!1,title:``,data:null}),[Y,X]=(0,v.useState)({open:!1,title:``,message:``,onConfirm:()=>{}}),Z=(0,v.useCallback)(async()=>{if(E){O(null),R(``),B(`mission`),H(``),W(``),K(!1),N(``),j(!1);return}if(C)try{j(!0);let e=await i(C);O(e),R(e.name),B(e.category),H(e.content),W(e.description??``),K(!1),N(``)}catch(t){O(null),N(t instanceof Error?t.message:e(`Failed to load prompt template.`))}finally{j(!1)}},[E,C,e]);(0,v.useEffect)(()=>{Z()},[Z]);function Q(e){R(e),K(!0)}function oe(e){B(e),K(!0)}function se(e){H(e),K(!0)}function ce(e){W(e),K(!0)}async function le(){let t=L.trim(),n=z.trim(),r=U.trim();if(E){if(!t){N(e(`Template name is required.`));return}if(!n){N(e(`Template category is required.`));return}if(!V.trim()){N(e(`Template content is required.`));return}try{F(!0);let i=await a({name:t,category:n,content:V,description:r||void 0,active:!0});O(i),R(i.name),B(i.category),H(i.content),W(i.description??``),K(!1),N(``),I(`success`,e(`Template "{{name}}" created.`,{name:i.name})),w(`/prompt-templates/${encodeURIComponent(i.name)}`,{replace:!0})}catch(t){N(t instanceof Error?t.message:e(`Create failed.`))}finally{F(!1)}return}if(!(!C||!D))try{F(!0);let t=await ee(C,{content:V,description:r||void 0});O(t),R(t.name),B(t.category),H(t.content),W(t.description??``),K(!1),N(``),I(`success`,e(`Template saved.`))}catch(t){N(t instanceof Error?t.message:e(`Save failed.`))}finally{F(!1)}}async function ue(){if(D)try{F(!0);let t=await a(g(D));I(`success`,e(`Template "{{name}}" duplicated.`,{name:t.name})),w(`/prompt-templates/${encodeURIComponent(t.name)}`)}catch(t){N(t instanceof Error?t.message:e(`Duplicate failed.`))}finally{F(!1)}}function $(){!D||!D.isBuiltIn||X({open:!0,title:e(`Reset to Default`),message:e(`Reset "{{name}}" to its built-in default content? Your customizations will be lost.`,{name:D.name}),onConfirm:async()=>{X(e=>({...e,open:!1}));try{let t=await o(D.name);O(t),H(t.content),W(t.description??``),K(!1),I(`success`,e(`Template reset to default.`))}catch{N(e(`Reset failed.`))}}})}function de(e){let t=T.current;if(!t)return;let n=t.selectionStart,r=t.selectionEnd,i=V.substring(0,n)+e+V.substring(r);H(i),K(!0),requestAnimationFrame(()=>{t.focus(),t.selectionStart=n+e.length,t.selectionEnd=n+e.length})}return A?(0,y.jsx)(`p`,{className:`text-dim`,children:e(`Loading...`)}):!E&&M&&!D?(0,y.jsx)(f,{error:M,onClose:()=>N(``)}):!E&&!D?(0,y.jsx)(`p`,{className:`text-dim`,children:e(`Template not found.`)}):(0,y.jsxs)(`div`,{children:[(0,y.jsx)(d,{breadcrumb:(0,y.jsxs)(y.Fragment,{children:[(0,y.jsx)(te,{to:`/prompt-templates`,children:e(`Prompt Templates`)}),` `,(0,y.jsx)(`span`,{className:`breadcrumb-sep`,children:`>`}),` `,(0,y.jsx)(`span`,{children:E?e(`Create`):L})]}),title:E?e(`Create Prompt Template`):L,actions:(0,y.jsx)(y.Fragment,{children:E?(0,y.jsx)(h,{status:z||`mission`}):(0,y.jsxs)(y.Fragment,{children:[(0,y.jsx)(h,{status:D.category}),D.isBuiltIn&&(0,y.jsx)(h,{status:`Built-in`}),(0,y.jsx)(p,{id:`template-${D.name}`,items:[{label:`Duplicate`,onClick:()=>void ue()},{label:`View JSON`,onClick:()=>J({open:!0,title:e(`Template: {{name}}`,{name:D.name}),data:D})},...D.isBuiltIn&&k?[{label:`Reset to Default`,danger:!0,onClick:$}]:[]]})]})})}),(0,y.jsx)(f,{error:M,onClose:()=>N(``)}),(0,y.jsx)(m,{open:q.open,title:q.title,data:q.data,onClose:()=>J({open:!1,title:``,data:null})}),(0,y.jsx)(re,{open:Y.open,title:Y.title,message:Y.message,onConfirm:Y.onConfirm,onCancel:()=>X(e=>({...e,open:!1}))}),(0,y.jsx)(`style`,{children:`
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
      `}),(0,y.jsx)(`div`,{className:`detail-grid`,children:E?(0,y.jsxs)(y.Fragment,{children:[(0,y.jsxs)(`label`,{className:`detail-field template-meta-field`,children:[(0,y.jsx)(`span`,{className:`detail-label`,children:e(`Name`)}),(0,y.jsx)(`input`,{className:`template-description-input`,value:L,onChange:e=>Q(e.target.value),placeholder:e(`mission.rules.custom`)})]}),(0,y.jsxs)(`label`,{className:`detail-field template-meta-field`,children:[(0,y.jsx)(`span`,{className:`detail-label`,children:e(`Category`)}),(0,y.jsx)(`input`,{className:`template-description-input`,list:`prompt-template-category-options`,value:z,onChange:e=>oe(e.target.value),placeholder:e(`mission`)}),(0,y.jsx)(`datalist`,{id:`prompt-template-category-options`,children:ae.map(e=>(0,y.jsx)(`option`,{value:e},e))})]}),(0,y.jsxs)(`div`,{className:`detail-field`,children:[(0,y.jsx)(`span`,{className:`detail-label`,children:e(`Type`)}),(0,y.jsx)(`span`,{children:e(`Custom template`)})]})]}):(0,y.jsxs)(y.Fragment,{children:[(0,y.jsxs)(`div`,{className:`detail-field`,children:[(0,y.jsx)(`span`,{className:`detail-label`,children:e(`ID`)}),(0,y.jsxs)(`span`,{className:`id-display`,children:[(0,y.jsx)(`span`,{className:`mono`,children:D.id}),(0,y.jsx)(u,{text:D.id})]})]}),(0,y.jsxs)(`div`,{className:`detail-field`,children:[(0,y.jsx)(`span`,{className:`detail-label`,children:e(`Active`)}),(0,y.jsx)(h,{status:D.active===!1?`Inactive`:`Active`})]}),(0,y.jsxs)(`div`,{className:`detail-field`,children:[(0,y.jsx)(`span`,{className:`detail-label`,children:e(`Created`)}),(0,y.jsx)(`span`,{children:n(D.createdUtc)})]}),(0,y.jsxs)(`div`,{className:`detail-field`,children:[(0,y.jsx)(`span`,{className:`detail-label`,children:e(`Last Updated`)}),(0,y.jsx)(`span`,{children:D.lastUpdateUtc?n(D.lastUpdateUtc):`-`})]})]})}),(0,y.jsxs)(`div`,{className:`template-editor-layout`,children:[(0,y.jsxs)(`div`,{className:`template-editor-panel`,children:[(0,y.jsxs)(`label`,{style:{fontSize:`0.85em`,color:`var(--text-dim)`},children:[e(`Description`),(0,y.jsx)(`input`,{type:`text`,className:`template-description-input`,value:U,onChange:e=>ce(e.target.value),placeholder:e(`Template description...`)})]}),(0,y.jsxs)(`div`,{style:{display:`flex`,justifyContent:`space-between`,alignItems:`center`},children:[(0,y.jsxs)(`label`,{style:{fontSize:`0.85em`,color:`var(--text-dim)`,margin:0},children:[e(`Template Content`),G&&(0,y.jsx)(`span`,{className:`template-dirty-indicator`,title:e(`Unsaved changes`)})]}),(0,y.jsxs)(`span`,{className:`template-char-count`,children:[V.length,` `,e(`characters`)]})]}),(0,y.jsx)(`textarea`,{ref:T,className:`template-editor-textarea`,value:V,onChange:e=>se(e.target.value),rows:30,spellCheck:!1}),(0,y.jsxs)(`div`,{className:`template-editor-actions`,children:[(0,y.jsx)(`button`,{className:`btn btn-primary`,onClick:le,disabled:P||!G||!k||E&&(!L.trim()||!z.trim()||!V.trim()),children:e(P?`Saving...`:`Save`)}),D?.isBuiltIn&&(0,y.jsx)(`button`,{className:`btn`,onClick:$,disabled:P,children:e(`Reset to Default`)}),(0,y.jsx)(`button`,{className:`btn`,onClick:()=>w(`/prompt-templates`),children:e(`Back`)})]})]}),(0,y.jsxs)(`div`,{className:`template-param-panel`,children:[(0,y.jsx)(`h4`,{children:e(`Parameters`)}),(0,y.jsx)(`p`,{style:{fontSize:`0.78em`,color:`var(--text-dim)`,margin:`0 0 0.75rem 0`},children:e(`Click a parameter to insert it at the cursor position.`)}),ie.map(t=>(0,y.jsxs)(`div`,{className:`template-param-group`,children:[(0,y.jsx)(`div`,{className:`template-param-group-label`,children:e(t.label)}),t.params.map(t=>(0,y.jsxs)(`div`,{className:`template-param-item`,onClick:()=>de(t.name),title:e(`Insert {{name}} -- {{description}}`,{name:t.name,description:e(t.description)}),children:[(0,y.jsx)(`span`,{className:`template-param-name`,children:t.name}),(0,y.jsx)(`span`,{className:`template-param-desc`,children:e(t.description)})]},t.name))]},t.label))]})]})]})}export{b as default};
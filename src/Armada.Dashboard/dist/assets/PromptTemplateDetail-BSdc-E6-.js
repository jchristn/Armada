import{a as e,c as t,i as n,n as r}from"./LocaleContext-BW_CQNtr.js";import{H as i,Si as a,xa as o,zn as ee}from"./client-C2ZXEh3A.js";import{n as s}from"./AuthContext-Da46PDGC.js";import{B as c,G as l,K as te,L as ne,O as u,U as re}from"./index-CMDXvWaO.js";import{t as ie}from"./PageHeader-Ey5ZeMNb.js";import{t as d}from"./ErrorModal-CSFyRBDs.js";import{t as f}from"./ActionMenu-B7-NrVxw.js";import{t as p}from"./StatusBadge-Bonm-eya.js";import{t as m}from"./JsonViewer-C5gA8-5S.js";import{c as h}from"./duplicates-CUTBw3rG.js";import{n as g}from"./scoping-CZkYCmjW.js";import{a as _,o as v}from"./configuration-BvcQqnam.js";var y=t(e(),1),b=n(),x=v,S=_;function C(){let{t:e,formatDateTime:t}=r(),{isAdmin:n,isTenantAdmin:_,user:v}=s(),C={isAdmin:n,isTenantAdmin:_,tenantId:v?.user?.tenantId,userId:v?.user?.id},{name:w}=te(),T=l(),E=(0,y.useRef)(null),D=!w,[O,k]=(0,y.useState)(null),A=D?!0:!O||g(C,O),[j,M]=(0,y.useState)(!0),[N,P]=(0,y.useState)(``),[F,I]=(0,y.useState)(!1),{pushToast:L}=c(),[R,z]=(0,y.useState)(``),[B,V]=(0,y.useState)(`mission`),[H,U]=(0,y.useState)(``),[W,G]=(0,y.useState)(``),[K,q]=(0,y.useState)(!1),[J,Y]=(0,y.useState)({open:!1,title:``,data:null}),[X,Z]=(0,y.useState)({open:!1,title:``,message:``,onConfirm:()=>{}}),Q=(0,y.useCallback)(async()=>{if(D){k(null),z(``),V(`mission`),U(``),G(``),q(!1),P(``),M(!1);return}if(w)try{M(!0);let e=await ee(w);k(e),z(e.name),V(e.category),U(e.content),G(e.description??``),q(!1),P(``)}catch(t){k(null),P(t instanceof Error?t.message:e(`Failed to load prompt template.`))}finally{M(!1)}},[D,w,e]);(0,y.useEffect)(()=>{Q()},[Q]);function ae(e){z(e),q(!0)}function oe(e){V(e),q(!0)}function se(e){U(e),q(!0)}function ce(e){G(e),q(!0)}async function le(){let t=R.trim(),n=B.trim(),r=W.trim();if(D){if(!t){P(e(`Template name is required.`));return}if(!n){P(e(`Template category is required.`));return}if(!H.trim()){P(e(`Template content is required.`));return}try{I(!0);let a=await i({name:t,category:n,content:H,description:r||void 0,active:!0});k(a),z(a.name),V(a.category),U(a.content),G(a.description??``),q(!1),P(``),L(`success`,e(`Template "{{name}}" created.`,{name:a.name})),T(`/prompt-templates/${encodeURIComponent(a.name)}`,{replace:!0})}catch(t){P(t instanceof Error?t.message:e(`Create failed.`))}finally{I(!1)}return}if(!(!w||!O))try{I(!0);let t=await o(w,{content:H,description:r||void 0});k(t),z(t.name),V(t.category),U(t.content),G(t.description??``),q(!1),P(``),L(`success`,e(`Template saved.`))}catch(t){P(t instanceof Error?t.message:e(`Save failed.`))}finally{I(!1)}}async function ue(){if(O)try{I(!0);let t=await i(h(O));L(`success`,e(`Template "{{name}}" duplicated.`,{name:t.name})),T(`/prompt-templates/${encodeURIComponent(t.name)}`)}catch(t){P(t instanceof Error?t.message:e(`Duplicate failed.`))}finally{I(!1)}}function $(){!O||!O.isBuiltIn||Z({open:!0,title:e(`Reset to Default`),message:e(`Reset "{{name}}" to its built-in default content? Your customizations will be lost.`,{name:O.name}),onConfirm:async()=>{Z(e=>({...e,open:!1}));try{let t=await a(O.name);k(t),U(t.content),G(t.description??``),q(!1),L(`success`,e(`Template reset to default.`))}catch{P(e(`Reset failed.`))}}})}function de(e){let t=E.current;if(!t)return;let n=t.selectionStart,r=t.selectionEnd,i=H.substring(0,n)+e+H.substring(r);U(i),q(!0),requestAnimationFrame(()=>{t.focus(),t.selectionStart=n+e.length,t.selectionEnd=n+e.length})}return j?(0,b.jsx)(`p`,{className:`text-dim`,children:e(`Loading...`)}):!D&&N&&!O?(0,b.jsx)(d,{error:N,onClose:()=>P(``)}):!D&&!O?(0,b.jsx)(`p`,{className:`text-dim`,children:e(`Template not found.`)}):(0,b.jsxs)(`div`,{children:[(0,b.jsx)(ie,{breadcrumb:(0,b.jsxs)(b.Fragment,{children:[(0,b.jsx)(re,{to:`/prompt-templates`,children:e(`Prompt Templates`)}),` `,(0,b.jsx)(`span`,{className:`breadcrumb-sep`,children:`>`}),` `,(0,b.jsx)(`span`,{children:D?e(`Create`):R})]}),title:D?e(`Create Prompt Template`):R,actions:(0,b.jsx)(b.Fragment,{children:D?(0,b.jsx)(p,{status:B||`mission`}):(0,b.jsxs)(b.Fragment,{children:[(0,b.jsx)(p,{status:O.category}),O.isBuiltIn&&(0,b.jsx)(p,{status:`Built-in`}),(0,b.jsx)(f,{id:`template-${O.name}`,items:[{label:`Duplicate`,onClick:()=>void ue()},{label:`View JSON`,onClick:()=>Y({open:!0,title:e(`Template: {{name}}`,{name:O.name}),data:O})},...O.isBuiltIn&&A?[{label:`Reset to Default`,danger:!0,onClick:$}]:[]]})]})})}),(0,b.jsx)(d,{error:N,onClose:()=>P(``)}),(0,b.jsx)(m,{open:J.open,title:J.title,data:J.data,onClose:()=>Y({open:!1,title:``,data:null})}),(0,b.jsx)(ne,{open:X.open,title:X.title,message:X.message,onConfirm:X.onConfirm,onCancel:()=>Z(e=>({...e,open:!1}))}),(0,b.jsx)(`style`,{children:`
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
      `}),(0,b.jsx)(`div`,{className:`detail-grid`,children:D?(0,b.jsxs)(b.Fragment,{children:[(0,b.jsxs)(`label`,{className:`detail-field template-meta-field`,children:[(0,b.jsx)(`span`,{className:`detail-label`,children:e(`Name`)}),(0,b.jsx)(`input`,{className:`template-description-input`,value:R,onChange:e=>ae(e.target.value),placeholder:e(`mission.rules.custom`)})]}),(0,b.jsxs)(`label`,{className:`detail-field template-meta-field`,children:[(0,b.jsx)(`span`,{className:`detail-label`,children:e(`Category`)}),(0,b.jsx)(`input`,{className:`template-description-input`,list:`prompt-template-category-options`,value:B,onChange:e=>oe(e.target.value),placeholder:e(`mission`)}),(0,b.jsx)(`datalist`,{id:`prompt-template-category-options`,children:S.map(e=>(0,b.jsx)(`option`,{value:e},e))})]}),(0,b.jsxs)(`div`,{className:`detail-field`,children:[(0,b.jsx)(`span`,{className:`detail-label`,children:e(`Type`)}),(0,b.jsx)(`span`,{children:e(`Custom template`)})]})]}):(0,b.jsxs)(b.Fragment,{children:[(0,b.jsxs)(`div`,{className:`detail-field`,children:[(0,b.jsx)(`span`,{className:`detail-label`,children:e(`ID`)}),(0,b.jsxs)(`span`,{className:`id-display`,children:[(0,b.jsx)(`span`,{className:`mono`,children:O.id}),(0,b.jsx)(u,{text:O.id})]})]}),(0,b.jsxs)(`div`,{className:`detail-field`,children:[(0,b.jsx)(`span`,{className:`detail-label`,children:e(`Active`)}),(0,b.jsx)(p,{status:O.active===!1?`Inactive`:`Active`})]}),(0,b.jsxs)(`div`,{className:`detail-field`,children:[(0,b.jsx)(`span`,{className:`detail-label`,children:e(`Created`)}),(0,b.jsx)(`span`,{children:t(O.createdUtc)})]}),(0,b.jsxs)(`div`,{className:`detail-field`,children:[(0,b.jsx)(`span`,{className:`detail-label`,children:e(`Last Updated`)}),(0,b.jsx)(`span`,{children:O.lastUpdateUtc?t(O.lastUpdateUtc):`-`})]})]})}),(0,b.jsxs)(`div`,{className:`template-editor-layout`,children:[(0,b.jsxs)(`div`,{className:`template-editor-panel`,children:[(0,b.jsxs)(`label`,{style:{fontSize:`0.85em`,color:`var(--text-dim)`},children:[e(`Description`),(0,b.jsx)(`input`,{type:`text`,className:`template-description-input`,value:W,onChange:e=>ce(e.target.value),placeholder:e(`Template description...`)})]}),(0,b.jsxs)(`div`,{style:{display:`flex`,justifyContent:`space-between`,alignItems:`center`},children:[(0,b.jsxs)(`label`,{style:{fontSize:`0.85em`,color:`var(--text-dim)`,margin:0},children:[e(`Template Content`),K&&(0,b.jsx)(`span`,{className:`template-dirty-indicator`,title:e(`Unsaved changes`)})]}),(0,b.jsxs)(`span`,{className:`template-char-count`,children:[H.length,` `,e(`characters`)]})]}),(0,b.jsx)(`textarea`,{ref:E,className:`template-editor-textarea`,"aria-label":e(`Template Content`),value:H,onChange:e=>se(e.target.value),rows:30,spellCheck:!1}),(0,b.jsxs)(`div`,{className:`template-editor-actions`,children:[(0,b.jsx)(`button`,{className:`btn btn-primary`,onClick:le,disabled:F||!K||!A||D&&(!R.trim()||!B.trim()||!H.trim()),children:e(F?`Saving...`:`Save`)}),O?.isBuiltIn&&(0,b.jsx)(`button`,{className:`btn`,onClick:$,disabled:F,children:e(`Reset to Default`)}),(0,b.jsx)(`button`,{className:`btn`,onClick:()=>T(`/prompt-templates`),children:e(`Back`)})]})]}),(0,b.jsxs)(`div`,{className:`template-param-panel`,role:`region`,"aria-label":e(`Parameters`),tabIndex:0,children:[(0,b.jsx)(`h4`,{children:e(`Parameters`)}),(0,b.jsx)(`p`,{style:{fontSize:`0.78em`,color:`var(--text-dim)`,margin:`0 0 0.75rem 0`},children:e(`Click a parameter to insert it at the cursor position.`)}),x.map(t=>(0,b.jsxs)(`div`,{className:`template-param-group`,children:[(0,b.jsx)(`div`,{className:`template-param-group-label`,children:e(t.label)}),t.params.map(t=>(0,b.jsxs)(`div`,{className:`template-param-item`,onClick:()=>de(t.name),title:e(`Insert {{name}} -- {{description}}`,{name:t.name,description:e(t.description)}),children:[(0,b.jsx)(`span`,{className:`template-param-name`,children:t.name}),(0,b.jsx)(`span`,{className:`template-param-desc`,children:e(t.description)})]},t.name))]},t.label))]})]})]})}export{C as default};
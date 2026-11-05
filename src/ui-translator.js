// Runs in a named isolated CDP world, sharing only the DOM with the application.
(function installChineseUI(dictionary) {
  'use strict';
  if (globalThis.__agChineseUI) return globalThis.__agChineseUI.status();
  if (location.protocol !== 'https:' || location.hostname !== '127.0.0.1') return { active: false };
  const scope = [
    '.settings-modal-container', '[data-testid="title-menu-bar"]',
    '[data-testid="sidebar-navigation-items"]', '[data-testid="settings-button"]',
    '[data-testid="home-greeting"]', '[data-testid="pane-titlebar-toolbar"]',
    '[data-testid="input-box-bottom-toolbar"]', '[data-testid="model-selector-trigger"]',
    '[data-testid="permissions-selector-trigger"]', '[data-testid="voice-live-pill"]',
    '[data-testid="app-icon-new-conversation-button"]', '[data-testid="sidebar-toggle"]',
    '[data-testid="title-menu-bar-option"]', '[data-testid="context-menu-popup"]',
    '[data-testid="context-menu-submenu-popup"]', '[role="menu"]', '[role="tooltip"]',
    '[data-testid="menu-option-label"]', '[data-testid="menu-option-description"]',
    '[data-testid="menu-section-header"]', '[data-testid="section-header"]'
  ].join(',');
  const protectedContent = [
    'script', 'style', 'svg', 'code', 'pre', 'kbd', 'textarea', 'input',
    '[contenteditable]:not([contenteditable="false"])', '.monaco-editor', '.xterm',
    '.prose', '[data-lexical-editor]', '[data-language]',
    '[data-testid="user-message-content"]', '[data-testid="user-message-bubble"]',
    '[data-testid="workspace-customizations-view"]', '[data-testid="project-customizations-content"]',
    '[data-testid="project-selector-item"]', '[data-testid="project-selector-github-repo-item"]',
    '[data-testid="project-header"]', '[data-testid="conversation-navigator-card"]',
    '[data-testid="conversation-navigator-turn"]', '[data-testid="terminal-surface"]',
    '[data-testid="automation-instructions-markdown"]', '[data-testid="builtin-browser-download-dir-value"]',
    '[data-testid="voice-settings-name"]', '[data-testid="voice-settings-mic"]'
  ].join(',');
  const texts = new Map(), attrs = new Map();
  let active = true, timer = null, observer, rootReady = false;
  const pending = new Set();
  const attributes = ['title', 'aria-label', 'placeholder'];
  function translate(value) {
    if (!value || !value.trim()) return value;
    const key = value.trim();
    if (Object.prototype.hasOwnProperty.call(dictionary, key)) {
      return value.slice(0,value.indexOf(key)) + dictionary[key] + value.slice(value.indexOf(key)+key.length);
    }
    return value;
  }
  function eligible(element) {
    return element && !element.closest(protectedContent) && !!element.closest(scope);
  }
  function textNode(node) {
    if (!eligible(node.parentElement)) return;
    const original = node.data, translated = translate(original);
    if (original === translated) return;
    texts.set(node, { original, translated });
    node.data = translated;
  }
  function elementNode(element) {
    // Never alter values, input names, role, IDs, data attributes or setting state.
    if (!eligible(element)) return;
    for (const name of attributes) {
      const original = element.getAttribute(name);
      const translated = translate(original);
      if (original === translated) continue;
      let records = attrs.get(element);
      if (!records) attrs.set(element, records = new Map());
      records.set(name, { original, translated });
      element.setAttribute(name, translated);
    }
  }
  function scan(root) {
    if (root.nodeType === Node.TEXT_NODE) { textNode(root); return; }
    if (root.nodeType !== Node.ELEMENT_NODE) return;
    elementNode(root);
    const walk = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
      acceptNode(node) {
        if (node.nodeType === Node.ELEMENT_NODE && node.matches(protectedContent)) return NodeFilter.FILTER_REJECT;
        return NodeFilter.FILTER_ACCEPT;
      }
    });
    while (walk.nextNode()) {
      const node=walk.currentNode;
      if (node.nodeType === Node.TEXT_NODE) textNode(node); else elementNode(node);
    }
  }
  function observe() {
    observer.observe(document.documentElement, {subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:attributes});
  }
  function flush() {
    timer = null;
    if (!active) return;
    observer.disconnect();
    try {
      if (!rootReady) rootReady = !!document.querySelector('[data-testid="title-menu-bar"]');
      if (rootReady) for (const root of pending) if (root.isConnected) scan(root);
      for (const node of texts.keys()) if (!node.isConnected) texts.delete(node);
      for (const node of attrs.keys()) if (!node.isConnected) attrs.delete(node);
    } finally { pending.clear(); if (active) observe(); }
  }
  function queue(node) { pending.add(node); if (timer === null) timer = setTimeout(flush, 80); }
  observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === 'childList') for (const node of record.addedNodes) queue(node);
      else queue(record.target);
    }
  });
  function restore() {
    document.removeEventListener('ag-chinese-restore-v1', restore);
    active = false; observer.disconnect(); if (timer !== null) clearTimeout(timer); pending.clear();
    let count=0;
    for (const [node, record] of texts) {
      if (node.isConnected && node.data === record.translated) { node.data=record.original; count++; }
    }
    for (const [element, records] of attrs) if (element.isConnected) {
      for (const [name, record] of records) if (element.getAttribute(name) === record.translated) element.setAttribute(name,record.original);
    }
    texts.clear(); attrs.clear(); delete globalThis.__agChineseUI;
    return {restored:count};
  }
  globalThis.__agChineseUI = {
    restore,
    status: () => ({active,ready:rootReady,translated:texts.size,attributes:attrs.size})
  };
  document.addEventListener('ag-chinese-restore-v1', restore);
  if (document.documentElement) { observe(); queue(document.documentElement); }
  else document.addEventListener('DOMContentLoaded',()=>{if(active){observe();queue(document.documentElement);}}, {once:true});
  return globalThis.__agChineseUI.status();
})(__AG_CHINESE_DICTIONARY__);

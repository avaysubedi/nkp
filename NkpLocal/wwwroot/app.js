// NKP Law Judgments Mobile PWA & Offline Engine
let currentPage = 1;
let currentSearch = '';
let currentCourt = '';
let currentCategory = '';
let currentTopic = '';
let currentFontSize = 1.05;
let dbInstance = null;

// DOM Elements
const searchInput = document.getElementById('searchInput');
const clearSearchBtn = document.getElementById('clearSearchBtn');
const categoryPills = document.getElementById('categoryPills');
const topicPills = document.getElementById('topicPills');
const courtPills = document.getElementById('courtPills');
const judgmentsList = document.getElementById('judgmentsList');
const resultsCount = document.getElementById('resultsCount');
const dataSourceBadge = document.getElementById('dataSourceBadge');
const pagination = document.getElementById('pagination');
const offlineBadge = document.getElementById('offlineBadge');
const offlineStatusText = document.getElementById('offlineStatusText');

const syncHeadline = document.getElementById('syncHeadline');
const syncSubtext = document.getElementById('syncSubtext');
const syncOfflineBtn = document.getElementById('syncOfflineBtn');
const syncIcon = document.getElementById('syncIcon');
const syncBtnLabel = document.getElementById('syncBtnLabel');

const detailModal = document.getElementById('detailModal');
const modalDecisionNo = document.getElementById('modalDecisionNo');
const modalCaseName = document.getElementById('modalCaseName');
const modalMetaGrid = document.getElementById('modalMetaGrid');
const modalText = document.getElementById('modalText');

const fontIncrease = document.getElementById('fontIncrease');
const fontDecrease = document.getElementById('fontDecrease');
const copyTextBtn = document.getElementById('copyTextBtn');
const closeModalBtn = document.getElementById('closeModalBtn');

// ==========================================
// 1. INDEXEDDB LOCAL STORAGE ENGINE
// ==========================================

function openIndexedDB() {
  return new Promise((resolve, reject) => {
    if (dbInstance) {
      resolve(dbInstance);
      return;
    }
    const request = indexedDB.open('NKPOfflineDB', 2);

    request.onupgradeneeded = (e) => {
      const db = e.target.result;
      let store;
      if (!db.objectStoreNames.contains('judgments')) {
        store = db.createObjectStore('judgments', { keyPath: 'id' });
      } else {
        store = e.target.transaction.objectStore('judgments');
      }
      if (!store.indexNames.contains('court')) {
        store.createIndex('court', 'court', { unique: false });
      }
      if (!store.indexNames.contains('decisionNumber')) {
        store.createIndex('decisionNumber', 'decisionNumber', { unique: false });
      }
      if (!store.indexNames.contains('category')) {
        store.createIndex('category', 'category', { unique: false });
      }
    };

    request.onsuccess = (e) => {
      dbInstance = e.target.result;
      resolve(dbInstance);
    };

    request.onerror = (e) => {
      console.error('IndexedDB Error:', e.target.error);
      reject(e.target.error);
    };
  });
}

async function saveJudgmentsToDB(items) {
  const db = await openIndexedDB();
  return new Promise((resolve, reject) => {
    const tx = db.transaction('judgments', 'readwrite');
    const store = tx.objectStore('judgments');
    items.forEach(item => {
      store.put(item);
    });
    tx.oncomplete = () => resolve(true);
    tx.onerror = (e) => reject(e.target.error);
  });
}

async function getLocalJudgmentsCount() {
  try {
    const db = await openIndexedDB();
    return new Promise((resolve) => {
      const tx = db.transaction('judgments', 'readonly');
      const store = tx.objectStore('judgments');
      const countReq = store.count();
      countReq.onsuccess = () => resolve(countReq.result || 0);
      countReq.onerror = () => resolve(0);
    });
  } catch (err) {
    return 0;
  }
}

async function getAllLocalJudgments() {
  const db = await openIndexedDB();
  return new Promise((resolve, reject) => {
    const tx = db.transaction('judgments', 'readonly');
    const store = tx.objectStore('judgments');
    const req = store.getAll();
    req.onsuccess = () => resolve(req.result || []);
    req.onerror = (e) => reject(e.target.error);
  });
}

async function getLocalJudgmentById(id) {
  const db = await openIndexedDB();
  return new Promise((resolve) => {
    const tx = db.transaction('judgments', 'readonly');
    const store = tx.objectStore('judgments');
    const req = store.get(Number(id));
    req.onsuccess = () => resolve(req.result || null);
    req.onerror = () => resolve(null);
  });
}

async function searchLocalJudgments(query, court, category, topic, page = 1, pageSize = 15) {
  const allItems = await getAllLocalJudgments();
  const q = (query || '').trim().toLowerCase();
  const c = (court || '').trim().toLowerCase();
  const cat = (category || '').trim().toLowerCase();
  const top = (topic || '').trim().toLowerCase();

  let filtered = allItems.filter(item => {
    // Court filter
    if (c && !(item.court || '').toLowerCase().includes(c)) {
      return false;
    }
    if (cat) {
      const storedCat = (item.category || '').trim().toLowerCase();
      const storedMudda = (item.muddaType || '').trim().toLowerCase();
      if (storedMudda === cat || storedCat === cat) {
        // official NKP type or high-level bucket
      } else if (!storedMudda && !storedCat) {
        const catHaystack = [item.caseName, item.caseNumber, item.summary].filter(Boolean).join(' ').toLowerCase();
        if (!catHaystack.includes(cat)) return false;
      } else {
        return false;
      }
    }
    if (top) {
      const tokens = (item.topics || '')
        .split('|')
        .map(s => s.trim().toLowerCase())
        .filter(Boolean);
      const aliases = top === 'बन्दी प्रत्यक्षीकरण' ? ['बन्दीप्रत्यक्षीकरण']
        : top === 'बन्दीप्रत्यक्षीकरण' ? ['बन्दी प्रत्यक्षीकरण']
        : [];
      const hasToken = tokens.includes(top) || aliases.some(a => tokens.includes(a));
      const caseName = (item.caseName || '').toLowerCase();
      const longer = ['अंश चलन', 'अंश जालसाजी', 'अंश नामसारी', 'अंशबन्डा', 'उत्प्रेषण / परमादेश']
        .map(s => s.toLowerCase())
        .filter(s => s !== top && s.includes(top));
      const nameHit = caseName.includes(top) && !longer.some(s => caseName.includes(s));
      if (!hasToken && !nameHit) {
        return false;
      }
    }
    // Search query filter
    if (q) {
      const haystack = [
        item.caseName,
        item.decisionNumber,
        item.caseNumber,
        item.court,
        item.laws,
        item.precedents,
        item.summary,
        item.fullText
      ].filter(Boolean).join(' ').toLowerCase();

      return haystack.includes(q);
    }
    return true;
  });

  filtered.sort((a, b) => {
    const byDate = nepaliDateKey(b).localeCompare(nepaliDateKey(a));
    if (byDate !== 0) return byDate;
    return (b.id || 0) - (a.id || 0);
  });

  const total = filtered.length;
  const totalPages = Math.ceil(total / pageSize) || 1;
  const startIndex = (page - 1) * pageSize;
  const pagedItems = filtered.slice(startIndex, startIndex + pageSize);

  return {
    judgments: pagedItems,
    total: total,
    page: page,
    pageSize: pageSize,
    totalPages: totalPages
  };
}

// ==========================================
// 2. APP INITIALIZATION & SYNC STATUS
// ==========================================

let appBooted = false;

function pageBase() {
  const path = window.location.pathname;
  if (path.endsWith('/') || path.split('/').pop().includes('.')) {
    return new URL('./', window.location.href);
  }
  return new URL(path + '/', window.location.origin);
}

function assetUrl(relativePath) {
  return new URL(relativePath, pageBase()).href;
}

function isLocalApi() {
  return location.hostname === 'localhost' || location.hostname === '127.0.0.1';
}

async function bootApp() {
  if (appBooted) return;
  appBooted = true;
  setupEventListeners();
  await updateSyncStatusUI();
  await syncWithServerSilently();
  await fetchJudgments();
  registerServiceWorker();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', bootApp);
} else {
  bootApp();
}

async function updateSyncStatusUI() {
  const count = await getLocalJudgmentsCount();
  const lastSyncTime = localStorage.getItem('nkp_last_synced_time');

  if (count === 0) {
    syncHeadline.textContent = `डेटा सिंक गरिएको छैन (${count} निर्णय)`;
    syncSubtext.textContent = 'साइटबाट डेटा सिंक गर्न दायाँ बटन थिच्नुहोस्।';
  } else {
    const formattedTime = formatRelativeTime(lastSyncTime);
    syncHeadline.textContent = `मोबाइल अफलाइन डेटाबेस (${count} निर्णय सुरक्षित)`;
    syncSubtext.textContent = `अन्तिम सिंक: ${formattedTime}`;
  }
}

function formatRelativeTime(isoString) {
  if (!isoString) return 'भएको छैन';
  const date = new Date(isoString);
  const now = new Date();
  const diffSec = Math.floor((now - date) / 1000);

  if (diffSec < 60) return 'भर्खरै';
  if (diffSec < 3600) return `${Math.floor(diffSec / 60)} मिनेट अघि`;
  if (diffSec < 86400) return `${Math.floor(diffSec / 3600)} घण्टा अघि`;
  return `${date.toLocaleDateString('ne-NP')} ${date.toLocaleTimeString('ne-NP', { hour: '2-digit', minute: '2-digit' })}`;
}

// ==========================================
// 3. SERVER SYNC ENGINE
// ==========================================

async function syncWithServer() {
  syncOfflineBtn.disabled = true;
  syncIcon.classList.add('spinning');
  syncBtnLabel.textContent = 'सिंक हुँदैछ...';

  try {
    const saved = await pullAllFromServer((done, total) => {
      syncBtnLabel.textContent = `सिंक ${done}/${total}`;
    });
    if (saved > 0) {
      await updateSyncStatusUI();
      showToast(`सफलतापूर्वक ${saved} निर्णयहरु अफलाइन सुरक्षित गरियो!`);
      fetchJudgments();
    } else {
      showToast('सर्भरमा कुनै डाटा भेटिएन।');
    }
  } catch (err) {
    console.error('Sync Error:', err);
    showToast('सिंक गर्न सकिएन। इन्टरनेट छैन वा डाटा फाइल भेटिएन।');
  } finally {
    syncOfflineBtn.disabled = false;
    syncIcon.classList.remove('spinning');
    syncBtnLabel.textContent = 'सर्भरसँग सिंक गर्नुहोस्';
  }
}

async function fetchManifest() {
  const res = await fetch(assetUrl('data/manifest.json'), { cache: 'no-store' });
  if (!res.ok) throw new Error('manifest missing');
  return res.json();
}

async function pullAllFromServer(onProgress) {
  const manifest = await fetchManifest();
  const totalPages = manifest.totalPages ?? manifest.TotalPages ?? 0;
  const total = manifest.total ?? manifest.Total ?? 0;
  if (!totalPages || total === 0) return 0;

  let saved = 0;
  for (let page = 1; page <= totalPages; page++) {
    const res = await fetch(assetUrl(`data/judgments-${page}.json`), { cache: 'no-store' });
    if (!res.ok) throw new Error('Static export page failed');
    const data = await res.json();
    const items = data.judgments || data.Judgments || [];
    if (!Array.isArray(items) || items.length === 0) break;

    await saveJudgmentsToDB(items);
    saved += items.length;
    if (onProgress) onProgress(saved, total);
  }

  if (saved > 0) {
    localStorage.setItem('nkp_last_synced_time', new Date().toISOString());
    localStorage.setItem('nkp_synced_count', String(saved));
    localStorage.setItem('nkp_data_generated_at', manifest.generatedAt || manifest.GeneratedAt || '');
  }
  return saved;
}

function showToast(message) {
  alert(message);
}

// ==========================================
// 4. EVENT LISTENERS
// ==========================================

function setupEventListeners() {
  syncOfflineBtn.addEventListener('click', syncWithServer);

  let debounceTimer;
  searchInput.addEventListener('input', (e) => {
    const val = e.target.value;
    clearSearchBtn.style.display = val ? 'block' : 'none';
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
      currentSearch = val;
      currentPage = 1;
      fetchJudgments();
    }, 300);
  });

  clearSearchBtn.addEventListener('click', () => {
    searchInput.value = '';
    clearSearchBtn.style.display = 'none';
    currentSearch = '';
    currentPage = 1;
    fetchJudgments();
  });

  if (categoryPills) {
    categoryPills.addEventListener('click', (e) => {
      if (e.target.classList.contains('pill')) {
        document.querySelectorAll('.pill-cat').forEach(p => p.classList.remove('active'));
        e.target.classList.add('active');
        currentCategory = e.target.getAttribute('data-category') || '';
        currentPage = 1;
        fetchJudgments();
      }
    });
  }

  if (topicPills) {
    topicPills.addEventListener('click', (e) => {
      if (e.target.classList.contains('pill')) {
        document.querySelectorAll('.pill-topic').forEach(p => p.classList.remove('active'));
        e.target.classList.add('active');
        currentTopic = e.target.getAttribute('data-topic') || '';
        currentPage = 1;
        fetchJudgments();
      }
    });
  }

  if (courtPills) {
    courtPills.addEventListener('click', (e) => {
      if (e.target.classList.contains('pill')) {
        document.querySelectorAll('.pill-court').forEach(p => p.classList.remove('active'));
        e.target.classList.add('active');
        currentCourt = e.target.getAttribute('data-court') || '';
        currentPage = 1;
        fetchJudgments();
      }
    });
  }

  fontIncrease.addEventListener('click', () => {
    currentFontSize += 0.15;
    modalText.style.fontSize = `${currentFontSize}rem`;
  });

  fontDecrease.addEventListener('click', () => {
    if (currentFontSize > 0.8) {
      currentFontSize -= 0.15;
      modalText.style.fontSize = `${currentFontSize}rem`;
    }
  });

  copyTextBtn.addEventListener('click', () => {
    const textToCopy = `${modalCaseName.innerText}\n\n${modalText.innerText}`;
    navigator.clipboard.writeText(textToCopy).then(() => {
      alert('पाठ प्रतिलिपि भयो (Copied to clipboard)!');
    });
  });

  closeModalBtn.addEventListener('click', () => {
    detailModal.classList.remove('active');
  });

  detailModal.addEventListener('click', (e) => {
    if (e.target === detailModal) {
      detailModal.classList.remove('active');
    }
  });
}

// ==========================================
// 5. HYBRID FETCH & SEARCH ROUTER
// ==========================================

async function fetchJudgments() {
  judgmentsList.innerHTML = `
    <div class="loading-state">
      <div class="spinner"></div>
      <p>नतिजाहरु खोज्दैछ...</p>
    </div>
  `;

  const localCount = await getLocalJudgmentsCount();
  setConnectionState(navigator.onLine);
  dataSourceBadge.textContent = localCount > 0
    ? 'डेटा स्रोत: अफलाइन IndexedDB'
    : 'डेटा स्रोत: डाटा छैन — सिंक गर्नुहोस्';

  const offlineData = await searchLocalJudgments(
    currentSearch, currentCourt, currentCategory, currentTopic, currentPage, 15);
  renderJudgments(offlineData.judgments, offlineData.total, offlineData.totalPages);
}

async function syncWithServerSilently() {
  try {
    const localCount = await getLocalJudgmentsCount();
    const manifest = await fetchManifest();
    const remoteTotal = manifest.total ?? manifest.Total ?? 0;
    const remoteGen = manifest.generatedAt || manifest.GeneratedAt || '';
    const lastGen = localStorage.getItem('nkp_data_generated_at');
    if (remoteTotal > 0 && (localCount === 0 || localCount !== remoteTotal || (remoteGen && remoteGen !== lastGen))) {
      await pullAllFromServer();
      await updateSyncStatusUI();
    }
  } catch (e) {
    // Offline or first publish without JSON — IndexedDB still works if already synced.
  }
}

function setConnectionState(isOnline) {
  if (isOnline) {
    offlineBadge.className = 'badge badge-online';
    offlineStatusText.textContent = 'Online';
  } else {
    offlineBadge.className = 'badge badge-offline';
    offlineStatusText.textContent = 'Offline (IndexedDB)';
  }
}

function nepaliDateKey(item) {
  const raw = String(item.decisionDate || item.decisionDateNepali || '');
  const ascii = raw.replace(/[०-९]/g, ch => String('०१२३४५६७८९'.indexOf(ch)));
  const match = ascii.match(/(\d{2,4})[./-](\d{1,2})[./-](\d{1,2})/);
  if (!match) return '';
  return `${match[1].padStart(4, '0')}-${match[2].padStart(2, '0')}-${match[3].padStart(2, '0')}`;
}

function getCategoryInfo(item) {
  if (item.muddaType) {
    const cls = item.category === 'देवानी' ? 'badge-civil'
      : item.category === 'रिट' ? 'badge-writ'
      : item.category === 'निवेदन' || item.category === 'विविध' ? 'badge-petition'
      : 'badge-criminal';
    return { name: item.muddaType, class: `badge-cat ${cls}` };
  }
  if (item.category === 'फौजदारी') {
    return { name: 'फौजदारी', class: 'badge-cat badge-criminal' };
  }
  if (item.category === 'देवानी') {
    return { name: 'देवानी', class: 'badge-cat badge-civil' };
  }
  if (item.category === 'रिट') {
    return { name: 'रिट', class: 'badge-cat badge-writ' };
  }
  if (item.category === 'निवेदन') {
    return { name: 'निवेदन', class: 'badge-cat badge-petition' };
  }
  const haystack = [item.caseName, item.summary].filter(Boolean).join(' ');
  if (haystack.includes('उत्प्रेषण') || haystack.includes('परमादेश') || haystack.includes('रिट')) {
    return { name: 'रिट', class: 'badge-cat badge-writ' };
  }
  if (haystack.includes('लागु औषध') || haystack.includes('कर्तव्य ज्यान') || haystack.includes('जबरजस्ती करणी') || haystack.includes('सवारी ज्यान')) {
    return { name: 'फौजदारी', class: 'badge-cat badge-criminal' };
  }
  if (haystack.includes('अंश') || haystack.includes('निज अर्जन') || haystack.includes('सगोल')) {
    return { name: 'देवानी', class: 'badge-cat badge-civil' };
  }
  return null;
}

function renderJudgments(items, total, totalPages) {
  resultsCount.textContent = `जम्मा नतिजा: ${total}`;

  if (!items || items.length === 0) {
    judgmentsList.innerHTML = `
      <div class="loading-state">
        <p>कुनै पनि नतिजा फेला परेन।</p>
      </div>
    `;
    pagination.innerHTML = '';
    return;
  }

  judgmentsList.innerHTML = items.map(item => {
    const catInfo = getCategoryInfo(item);
    const summarySnippet = item.summary || (item.fullText ? (item.fullText.length > 200 ? item.fullText.substring(0, 200) + '...' : item.fullText) : '');

    return `
    <div class="judgment-card">
      <div class="card-top">
        <span class="decision-badge">निर्णय नं. ${item.decisionNumber || '---'}</span>
        ${catInfo ? `<span class="${catInfo.class}">${catInfo.name}</span>` : ''}
        <span class="date-text">${item.decisionDate || 'मिति नखुलेको'}</span>
      </div>
      <div class="card-title">${item.caseName || 'मुद्दा विवरण'}</div>
      <div class="card-meta">
        ${item.caseNumber ? `<span class="meta-tag">मुद्दा नं: ${item.caseNumber}</span>` : ''}
        ${item.court ? `<span class="meta-tag">अदालत: ${item.court}</span>` : ''}
        ${item.muddaType ? `<span class="meta-tag">${item.muddaType}</span>` : ''}
        ${item.topics ? `<span class="meta-tag">${item.topics}</span>` : ''}
        ${item.laws ? `<span class="meta-tag">कानून: ${item.laws}</span>` : ''}
      </div>
      ${summarySnippet ? `<div class="card-summary-box"><strong>नजिर सार / मुख्य ठहर:</strong> ${summarySnippet}</div>` : ''}
      <div class="card-actions">
        <button class="btn-view-detail" onclick="openJudgmentDetail(${item.id})">📖 पूरा फैसला विवरण</button>
      </div>
    </div>
  `;
  }).join('');

  renderPagination(totalPages);
}

function renderPagination(totalPages) {
  if (totalPages <= 1) {
    pagination.innerHTML = '';
    return;
  }

  let html = '';
  if (currentPage > 1) {
    html += `<button class="page-btn" onclick="changePage(${currentPage - 1})">अघिल्लो</button>`;
  }
  html += `<span class="page-btn active">${currentPage} / ${totalPages}</span>`;
  if (currentPage < totalPages) {
    html += `<button class="page-btn" onclick="changePage(${currentPage + 1})">पछिल्लो</button>`;
  }
  pagination.innerHTML = html;
}

function changePage(page) {
  currentPage = page;
  fetchJudgments();
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

async function openJudgmentDetail(id) {
  let data = await getLocalJudgmentById(id);

  if ((!data || !data.fullText) && isLocalApi()) {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 2500);
      const res = await fetch(`/api/judgments/${id}`, { signal: controller.signal });
      clearTimeout(timeoutId);
      if (res.ok) {
        data = await res.json();
        if (data) await saveJudgmentsToDB([data]);
      }
    } catch (err) {
      console.log('Detail falling back to IndexedDB for ID:', id);
    }
  }

  if (!data) {
    data = await getLocalJudgmentById(id);
  }

  if (!data) {
    alert('विवरण भेटिएन।');
    return;
  }

  modalDecisionNo.textContent = `निर्णय नं. ${data.decisionNumber || '---'}`;
  modalCaseName.textContent = data.caseName || 'फैसला विवरण';

  modalMetaGrid.innerHTML = `
    <div class="meta-item"><strong>मुद्दा नं</strong>${data.caseNumber || '-'}</div>
    <div class="meta-item"><strong>फैसला मिति</strong>${data.decisionDate || '-'}</div>
    <div class="meta-item"><strong>अदालत</strong>${data.court || '-'}</div>
    <div class="meta-item"><strong>न्यायाधीशहरु</strong>${data.judges || '-'}</div>
    <div class="meta-item"><strong>पुनरावेदक / वादी</strong>${data.petitioner || '-'}</div>
    <div class="meta-item"><strong>प्रत्यर्थी / प्रतिवादी</strong>${data.respondent || '-'}</div>
    <div class="meta-item"><strong>सम्बद्ध कानून</strong>${data.laws || '-'}</div>
    <div class="meta-item"><strong>अवलम्बित नजिर</strong>${data.precedents || '-'}</div>
    ${data.detailUrl ? `<div class="meta-item"><strong>मूल स्रोत</strong><a href="${data.detailUrl}" target="_blank" rel="noopener">nkp.gov.np</a></div>` : ''}
  `;

  modalText.textContent = data.fullText || data.summary || 'सारांश उपलब्ध छैन।';

  if (!data.fullText && data.summary) {
    const hint = document.createElement('p');
    hint.style.opacity = '0.75';
    hint.style.marginBottom = '1rem';
    hint.textContent = 'अफलाइन सारांश देखाइएको छ। पूर्ण पाठ उपलब्ध भएपछि सिंक गर्दा आउँछ।';
    modalText.prepend(hint);
  }

  detailModal.classList.add('active');

  if (!data.fullText && isLocalApi() && navigator.onLine) {
    try {
      const res = await fetch(`/api/judgments/${id}?hydrate=true`);
      if (res.ok) {
        const hydrated = await res.json();
        if (hydrated && hydrated.fullText) {
          await saveJudgmentsToDB([hydrated]);
          modalText.textContent = hydrated.fullText;
        }
      }
    } catch (e) {
      // Keep summary if NKP is unreachable.
    }
  }
}

function registerServiceWorker() {
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register(assetUrl('sw.js')).then(() => {
      console.log('Service Worker Registered');
    }).catch(err => console.error('SW registration failed:', err));
  }
}

// Preview Text Preset Frontend Script

let presets = [];
let currentCategory = "すべて";
let contextTargetPresetId = null;

// DOM Elements
const previewInput = document.getElementById("previewTextInput");
const refreshBtn = document.getElementById("refreshBtn");
const addPresetBtn = document.getElementById("addPresetBtn");
const importBtn = document.getElementById("importBtn");
const categoryTabs = document.getElementById("categoryTabs");
const presetGrid = document.getElementById("presetGrid");
const contextMenu = document.getElementById("contextMenu");
const toast = document.getElementById("toast");

// C# へのメッセージ送信ヘルパー
function sendMessageToHost(action, data = {}) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(JSON.stringify({ action, data }));
    } else {
        console.log("Host message:", action, data);
    }
}

// C# からのメッセージハンドラ
if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", (event) => {
        try {
            const message = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
            handleHostMessage(message);
        } catch (e) {
            console.error("Failed to parse host message:", e);
        }
    });
}

function handleHostMessage(message) {
    const { action, data } = message;
    switch (action) {
        case "setPresets":
            presets = data.presets || [];
            renderPresets();
            break;
        case "updatePreviewImage":
            updateCardThumbnail(data.presetId, data.base64);
            break;
        case "showToast":
            showToast(data.message, data.type);
            break;
        default:
            console.log("Unhandled action:", action, data);
    }
}

// プリセット描画
function renderPresets() {
    presetGrid.innerHTML = "";

    const filtered = currentCategory === "すべて" 
        ? presets 
        : presets.filter(p => p.category === currentCategory);

    if (filtered.length === 0) {
        presetGrid.innerHTML = `<div style="grid-column: 1/-1; text-align: center; color: var(--text-secondary); padding: 40px;">プリセットがありません。「＋ 登録」から追加してください。</div>`;
        return;
    }

    filtered.forEach(preset => {
        const card = document.createElement("div");
        card.className = "preset-card";
        card.dataset.id = preset.id;

        // タグバッジ生成
        let badgesHtml = "";
        if (preset.apply.effects) badgesHtml += `<span class="badge effect">エフェクト</span>`;
        if (preset.apply.font) badgesHtml += `<span class="badge font">フォント</span>`;
        if (preset.apply.position) badgesHtml += `<span class="badge position">位置</span>`;

        // サムネイル画像
        const thumbSrc = preset.cachedThumbnailBase64 || "";
        const imgDisplay = thumbSrc ? `<img class="preview-img" src="${thumbSrc}" alt="preview">` : `<div class="preview-placeholder">描画中...</div>`;

        card.innerHTML = `
            <div class="preview-box" id="thumb_${preset.id}">
                ${imgDisplay}
            </div>
            <div class="card-info">
                <div class="preset-title" title="${preset.name}">${preset.name}</div>
                <div class="preset-tags">${badgesHtml}</div>
            </div>
        `;

        // 左クリック: 適用
        card.addEventListener("click", () => {
            sendMessageToHost("applyPreset", {
                presetId: preset.id,
                applyEffects: preset.apply.effects,
                applyFont: preset.apply.font,
                applyPosition: preset.apply.position
            });
        });

        // 右クリック: コンテキストメニュー表示
        card.addEventListener("contextmenu", (e) => {
            e.preventDefault();
            contextTargetPresetId = preset.id;
            showContextMenu(e.clientX, e.clientY, preset);
        });

        presetGrid.appendChild(card);

        // プレビューがまだ未生成なら描画要求を送る
        if (!preset.cachedThumbnailBase64) {
            requestPresetPreview(preset.id);
        }
    });
}

function updateCardThumbnail(presetId, base64) {
    const thumbBox = document.getElementById(`thumb_${presetId}`);
    if (thumbBox && base64) {
        thumbBox.innerHTML = `<img class="preview-img" src="${base64}" alt="preview">`;
    }
}

function requestPresetPreview(presetId) {
    sendMessageToHost("requestPreview", {
        presetId: presetId,
        previewText: previewInput.value
    });
}

// プレビュー文字列の変更
function onPreviewTextChanged() {
    const text = previewInput.value;
    sendMessageToHost("previewTextChanged", { previewText: text });
    // 全プレビューを再要求
    presets.forEach(p => {
        p.cachedThumbnailBase64 = null;
        const thumbBox = document.getElementById(`thumb_${p.id}`);
        if (thumbBox) thumbBox.innerHTML = `<div class="preview-placeholder">更新中...</div>`;
        requestPresetPreview(p.id);
    });
}

previewInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter") onPreviewTextChanged();
});
refreshBtn.addEventListener("click", onPreviewTextChanged);

// カテゴリ切り替え
categoryTabs.addEventListener("click", (e) => {
    if (e.target.classList.contains("tab-btn")) {
        categoryTabs.querySelectorAll(".tab-btn").forEach(btn => btn.classList.remove("active"));
        e.target.classList.add("active");
        currentCategory = e.target.dataset.category;
        renderPresets();
    }
});

// 新規登録
addPresetBtn.addEventListener("click", () => {
    const name = prompt("新規プリセット名を入力してください:", "カスタムテロップ");
    if (!name) return;

    sendMessageToHost("createPreset", {
        name: name,
        category: currentCategory === "すべて" ? "基本" : currentCategory,
        saveEffects: true,
        saveFont: true,
        savePosition: true
    });
});

// インポート
importBtn.addEventListener("click", () => {
    sendMessageToHost("importPresetRequested", {});
});

// 右クリックメニューの処理
function showContextMenu(x, y, preset) {
    contextMenu.style.display = "block";
    contextMenu.style.left = `${Math.min(x, window.innerWidth - 170)}px`;
    contextMenu.style.top = `${Math.min(y, window.innerHeight - 250)}px`;
}

function hideContextMenu() {
    contextMenu.style.display = "none";
    contextTargetPresetId = null;
}

window.addEventListener("click", () => hideContextMenu());

contextMenu.addEventListener("click", (e) => {
    const item = e.target.closest(".context-menu-item");
    if (!item || !contextTargetPresetId) return;

    const action = item.dataset.action;
    const preset = presets.find(p => p.id === contextTargetPresetId);
    if (!preset) return;

    switch (action) {
        case "apply":
            sendMessageToHost("applyPreset", {
                presetId: preset.id,
                applyEffects: preset.apply.effects,
                applyFont: preset.apply.font,
                applyPosition: preset.apply.position
            });
            break;
        case "apply-effect":
            sendMessageToHost("applyPreset", {
                presetId: preset.id,
                applyEffects: true,
                applyFont: false,
                applyPosition: false
            });
            break;
        case "apply-font":
            sendMessageToHost("applyPreset", {
                presetId: preset.id,
                applyEffects: false,
                applyFont: true,
                applyPosition: false
            });
            break;
        case "apply-position":
            sendMessageToHost("applyPreset", {
                presetId: preset.id,
                applyEffects: false,
                applyFont: false,
                applyPosition: true
            });
            break;
        case "duplicate":
            sendMessageToHost("duplicatePreset", { presetId: preset.id });
            break;
        case "rename":
            const newName = prompt("プリセットの新しい名前を入力してください:", preset.name);
            if (newName && newName !== preset.name) {
                sendMessageToHost("renamePreset", { presetId: preset.id, newName: newName });
            }
            break;
        case "export":
            sendMessageToHost("exportPreset", { presetId: preset.id });
            break;
        case "delete":
            if (confirm(`プリセット「${preset.name}」を削除しますか？`)) {
                sendMessageToHost("deletePreset", { presetId: preset.id });
            }
            break;
    }
    hideContextMenu();
});

// トースト表示
function showToast(message, type = "info") {
    toast.textContent = message;
    toast.className = `toast show ${type}`;
    setTimeout(() => {
        toast.className = "toast";
    }, 2500);
}

// 初期化要求
document.addEventListener("DOMContentLoaded", () => {
    sendMessageToHost("ready", {});
});

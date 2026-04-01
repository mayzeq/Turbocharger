// Базовый URL API
const API = '/api';

// Глобальные данные
let items = [];
let boms = [];
let currentRootId = null; // для структуры

// Инициализация после загрузки страницы
document.addEventListener('DOMContentLoaded', () => {
    initMenu();
    initItemModal();
    initBomModal();
    initOperationModal();
    loadItems();
    loadBoms();
});

// Переключение вкладок меню
function initMenu() {
    document.querySelectorAll('.menu-item').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.menu-item').forEach(b => b.classList.remove('active'));
            document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));

            btn.classList.add('active');
            const viewId = btn.dataset.view + '-view';
            document.getElementById(viewId).classList.add('active');

            // Загрузка данных при переключении
            if (btn.dataset.view === 'bom') {
                updateBomRootSelect();
            } else if (btn.dataset.view === 'hierarchy') {
                updateHierarchyRootSelect();
            } else if (btn.dataset.view === 'calculator') {
                updateCalcSelect();
            } else if (btn.dataset.view === 'warehouse') {
                loadWarehouseData();
            }
        });
    });
}

// ==================== Элементы (Items) ====================

async function loadItems() {
    try {
        const res = await fetch(`${API}/Item`);
        if (!res.ok) throw new Error(`Ошибка загрузки элементов: ${res.status}`);
        items = await res.json();

        // Пересчитываем стоимость для каждого элемента
        items = items.map(item => ({
            ...item,
            calculatedCost: item.currentQuantity * item.purchasePrice
        }));

        items.sort((a, b) => a.itemId - b.itemId);
        renderItems(items);

        // Обновляем все зависимые представления
        updateBomRootSelect();
        updateHierarchyRootSelect();
        updateCalcSelect();
        updateBomModalSelects();

        // Если активна вкладка склада, обновляем её
        if (document.getElementById('warehouse-view').classList.contains('active')) {
            loadWarehouseData();
        }
    } catch (err) {
        alert(err.message);
    }
}

function renderItems(itemsArray) {
    const tbody = document.getElementById('items-table');
    tbody.innerHTML = itemsArray.map(item => `
        <tr>
            <td><strong>${item.itemId}</strong>${item.componentId || ''}</td>
            <td>${item.itemName}</td>
            <td>${Math.floor(item.currentQuantity)}</td>
            <td>${item.purchasePrice.toFixed(2)}</td>
            <td>${(item.currentQuantity * item.purchasePrice).toFixed(2)}</td>
            <td class="actions">
                <button class="btn btn-sm btn-secondary" onclick="editItem(${item.itemId})">Изменить</button>
                <button class="btn btn-sm btn-danger" onclick="deleteItem(${item.itemId})">Удалить</button>
            </td>
         </tr>
    `).join('');
}

function initItemModal() {
    const modal = document.getElementById('item-modal');
    const openBtn = document.getElementById('add-item-btn');
    const closeBtn = document.getElementById('item-modal-close');
    const cancelBtn = document.getElementById('item-modal-cancel');
    const form = document.getElementById('item-form');

    if (openBtn) {
        openBtn.addEventListener('click', () => {
            document.getElementById('item-modal-title').textContent = 'Добавить элемент';
            document.getElementById('item-id').value = '';
            document.getElementById('item-name').value = '';
            document.getElementById('item-price').value = '';
            modal.classList.add('active');
        });
    }

    const closeModal = () => modal.classList.remove('active');
    if (closeBtn) closeBtn.addEventListener('click', closeModal);
    if (cancelBtn) cancelBtn.addEventListener('click', closeModal);

    if (form) {
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const id = document.getElementById('item-id').value;
            const data = {
                itemName: document.getElementById('item-name').value,
                purchasePrice: parseFloat(document.getElementById('item-price').value) || 0
            };

            const url = id ? `${API}/Item/${id}` : `${API}/Item`;
            const method = id ? 'PUT' : 'POST';

            try {
                const response = await fetch(url, {
                    method,
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(data)
                });
                if (!response.ok) {
                    const errorText = await response.text();
                    throw new Error(`Ошибка ${response.status}: ${errorText}`);
                }
                closeModal();
                await loadItems(); // Перезагружаем все элементы
                await loadBoms(); // Обновляем BOM для обновления отображения
            } catch (err) {
                alert('Не удалось сохранить элемент: ' + err.message);
            }
        });
    }
}

window.editItem = async function (id) {
    const item = items.find(i => i.itemId === id);
    if (!item) return;

    document.getElementById('item-modal-title').textContent = 'Редактировать элемент';
    document.getElementById('item-id').value = item.itemId;
    document.getElementById('item-name').value = item.itemName;
    document.getElementById('item-price').value = item.purchasePrice;
    document.getElementById('item-modal').classList.add('active');
};

window.deleteItem = async function (id) {
    if (!confirm('Удалить элемент?')) return;
    try {
        const response = await fetch(`${API}/Item/${id}`, { method: 'DELETE' });
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Ошибка ${response.status}: ${errorText}`);
        }
        await loadItems();
        await loadBoms();

        // Обновляем склад если активен
        if (document.getElementById('warehouse-view').classList.contains('active')) {
            loadWarehouseData();
        }
    } catch (err) {
        alert('Ошибка при удалении: ' + err.message);
    }
};

const searchInput = document.getElementById('search-input');
if (searchInput) {
    searchInput.addEventListener('input', (e) => {
        const query = e.target.value.toLowerCase();
        const filtered = items.filter(item => item.itemName.toLowerCase().includes(query));
        renderItems(filtered);
    });
}

// ==================== BOM (связи) ====================

async function loadBoms() {
    try {
        const res = await fetch(`${API}/Bom`);
        if (!res.ok) throw new Error(`Ошибка загрузки связей: ${res.status}`);
        boms = await res.json();

        const activeView = document.querySelector('.view.active');
        if (activeView) {
            if (activeView.id === 'bom-view') {
                if (currentRootId) loadBomTree();
            } else if (activeView.id === 'hierarchy-view') {
                loadHierarchyTree();
            }
        }
    } catch (err) {
        alert(err.message);
    }
}

function updateBomRootSelect() {
    const select = document.getElementById('bom-root-select');
    if (!select) return;

    const sortedItems = [...items].sort((a, b) => a.itemId - b.itemId);
    select.innerHTML = sortedItems.map(item =>
        `<option value="${item.itemId}">${item.itemId} — ${item.itemName}</option>`
    ).join('');

    if (items.length > 0) {
        currentRootId = parseInt(select.value);
        loadBomTree();
    }

    updateBomModalSelects();
}

const bomRootSelect = document.getElementById('bom-root-select');
if (bomRootSelect) {
    bomRootSelect.addEventListener('change', function () {
        currentRootId = parseInt(this.value);
        loadBomTree();
    });
}

function loadBomTree() {
    const container = document.getElementById('bom-tree');
    if (!container) return;

    if (!currentRootId) {
        container.innerHTML = '<p class="placeholder">Выберите корневой элемент</p>';
        return;
    }

    const rootItem = items.find(i => i.itemId === currentRootId);
    if (!rootItem) {
        container.innerHTML = '<p class="placeholder">Корневой элемент не найден</p>';
        return;
    }

    const tree = buildTree(currentRootId);

    let html = `
        <div class="tree-item root-item">
            <div>
                <strong>${rootItem.itemId}</strong> — ${rootItem.itemName}
            </div>
        </div>
    `;

    if (tree && tree.length > 0) {
        html += renderBomTree(tree);
    } else {
        html += '<div class="tree-item" style="color: var(--text-secondary);">Нет дочерних элементов</div>';
    }

    container.innerHTML = html;
}

function buildTree(rootId) {
    const result = [];
    const children = boms.filter(b => b.parentId === rootId);
    for (const child of children) {
        const node = {
            bomId: child.bomId,
            componentId: child.componentId,
            quantity: child.quantity,
            item: items.find(i => i.itemId === child.componentId)
        };
        node.children = buildTree(child.componentId);
        result.push(node);
    }
    return result;
}

function renderBomTree(nodes, level = 0) {
    if (!nodes || nodes.length === 0) return '';

    let html = '';
    nodes.forEach(node => {
        const item = node.item || { itemId: node.componentId, itemName: 'Неизвестно' };
        html += `
            <div class="tree-item" style="margin-left: ${level * 20}px;">
                <div style="display: flex; gap: 10px; align-items: center; flex-wrap: wrap;">
                    <strong>${item.itemId}</strong> — ${item.itemName}
                    <span class="badge">×${node.quantity}</span>
                    <button class="btn btn-sm btn-secondary" onclick="editBom(${node.bomId})">Изменить связь</button>
                    <button class="btn btn-sm btn-secondary" onclick="editItemFromBom(${item.itemId})">Изменить элемент</button>
                    <button class="btn btn-sm btn-danger" onclick="deleteBom(${node.bomId})">Удалить</button>
                </div>
            </div>
        `;
        if (node.children && node.children.length > 0) {
            html += renderBomTree(node.children, level + 1);
        }
    });
    return html;
}

window.editBom = async function (bomId) {
    const bom = boms.find(b => b.bomId === bomId);
    if (!bom) return;

    document.getElementById('bom-modal-title').textContent = 'Редактировать связь';
    document.getElementById('bom-id').value = bom.bomId;
    document.getElementById('bom-parent').value = bom.parentId || '';
    document.getElementById('bom-component').value = bom.componentId;
    document.getElementById('bom-quantity').value = bom.quantity;
    document.getElementById('bom-modal').classList.add('active');
};

window.editItemFromBom = async function (itemId) {
    const item = items.find(i => i.itemId === itemId);
    if (!item) return;

    document.getElementById('item-modal-title').textContent = 'Редактировать элемент';
    document.getElementById('item-id').value = item.itemId;
    document.getElementById('item-name').value = item.itemName;
    document.getElementById('item-price').value = item.purchasePrice;
    document.getElementById('item-modal').classList.add('active');
};

window.deleteBom = async function (bomId) {
    if (!confirm('Удалить связь?')) return;
    try {
        const response = await fetch(`${API}/Bom/${bomId}`, { method: 'DELETE' });
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Ошибка ${response.status}: ${errorText}`);
        }
        await loadBoms();
    } catch (err) {
        alert('Ошибка при удалении связи: ' + err.message);
    }
};

function initBomModal() {
    const modal = document.getElementById('bom-modal');
    const openBtn = document.getElementById('add-bom-btn');
    const closeBtn = document.getElementById('bom-modal-close');
    const cancelBtn = document.getElementById('bom-modal-cancel');
    const form = document.getElementById('bom-form');

    if (openBtn) {
        openBtn.addEventListener('click', () => {
            if (!currentRootId) {
                alert('Сначала выберите корневой элемент');
                return;
            }
            document.getElementById('bom-modal-title').textContent = 'Добавить связь';
            document.getElementById('bom-id').value = '';
            document.getElementById('bom-parent').value = currentRootId;
            document.getElementById('bom-quantity').value = 1;
            modal.classList.add('active');
        });
    }

    const closeModal = () => modal.classList.remove('active');
    if (closeBtn) closeBtn.addEventListener('click', closeModal);
    if (cancelBtn) cancelBtn.addEventListener('click', closeModal);

    if (form) {
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const id = document.getElementById('bom-id').value;
            const data = {
                ParentId: document.getElementById('bom-parent').value ? parseInt(document.getElementById('bom-parent').value) : null,
                ComponentId: parseInt(document.getElementById('bom-component').value),
                Quantity: parseInt(document.getElementById('bom-quantity').value)
            };

            const url = id ? `${API}/Bom/${id}` : `${API}/Bom`;
            const method = id ? 'PUT' : 'POST';

            try {
                const response = await fetch(url, {
                    method,
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(data)
                });
                if (!response.ok) {
                    const errorText = await response.text();
                    throw new Error(`Ошибка ${response.status}: ${errorText}`);
                }
                closeModal();
                await loadBoms();
            } catch (err) {
                alert('Не удалось сохранить связь: ' + err.message);
            }
        });
    }
}

function updateBomModalSelects() {
    const parentSelect = document.getElementById('bom-parent');
    const componentSelect = document.getElementById('bom-component');

    if (!parentSelect || !componentSelect) return;

    const sortedItems = [...items].sort((a, b) => a.itemId - b.itemId);

    parentSelect.innerHTML = `<option value="">-- Без родителя (корень) --</option>` +
        sortedItems.map(item => `<option value="${item.itemId}">${item.itemId} — ${item.itemName}</option>`).join('');

    componentSelect.innerHTML = sortedItems.map(item =>
        `<option value="${item.itemId}">${item.itemId} — ${item.itemName}</option>`
    ).join('');
}

// ==================== Иерархия ====================

function updateHierarchyRootSelect() {
    const select = document.getElementById('hierarchy-root-select');
    if (!select) return;

    const sortedItems = [...items].sort((a, b) => a.itemId - b.itemId);
    select.innerHTML = sortedItems.map(item =>
        `<option value="${item.itemId}">${item.itemId} — ${item.itemName}</option>`
    ).join('');

    if (items.length > 0) {
        loadHierarchyTree();
    }
}

const hierarchyRootSelect = document.getElementById('hierarchy-root-select');
if (hierarchyRootSelect) {
    hierarchyRootSelect.addEventListener('change', loadHierarchyTree);
}

function loadHierarchyTree() {
    const rootId = parseInt(document.getElementById('hierarchy-root-select')?.value);
    const container = document.getElementById('hierarchy-tree');

    if (!container) return;

    if (!rootId) {
        container.innerHTML = '<p class="placeholder">Выберите корневой элемент</p>';
        return;
    }

    const rootItem = items.find(i => i.itemId === rootId);
    if (!rootItem) {
        container.innerHTML = '<p class="placeholder">Корневой элемент не найден</p>';
        return;
    }

    const tree = buildTree(rootId);
    renderHierarchyTree(tree, rootItem);
}

function renderHierarchyTree(children, rootItem) {
    const container = document.getElementById('hierarchy-tree');
    if (!container) return;

    function buildNodeHtml(node) {
        const item = node.item || { itemId: node.componentId, itemName: 'Неизвестно' };
        let childrenHtml = '';
        if (node.children && node.children.length > 0) {
            childrenHtml = '<ul>';
            node.children.forEach(child => {
                childrenHtml += buildNodeHtml(child);
            });
            childrenHtml += '</ul>';
        }
        return `
            <li>
                <div class="hierarchy-node">
                    <div class="hierarchy-info">
                        <span class="hierarchy-id">${item.itemId}</span>
                        <span class="hierarchy-name">${item.itemName}</span>
                    </div>
                    <span class="hierarchy-quantity">×${node.quantity}</span>
                </div>
                ${childrenHtml}
            </li>
        `;
    }

    let html = '<ul class="hierarchy-tree">';
    html += `
        <li>
            <div class="hierarchy-node root-node">
                <div class="hierarchy-info">
                    <span class="hierarchy-id">${rootItem.itemId}</span>
                    <span class="hierarchy-name"><strong>${rootItem.itemName}</strong></span>
                </div>
                <span class="hierarchy-quantity">×1</span>
            </div>
    `;

    if (children && children.length > 0) {
        html += '<ul>';
        children.forEach(child => {
            html += buildNodeHtml(child);
        });
        html += '</ul>';
    } else {
        html += '<ul><li style="color: var(--text-secondary); list-style: none;">Нет дочерних элементов</li></ul>';
    }

    html += '</li></ul>';
    container.innerHTML = html;
}

// ==================== Калькулятор ====================

function updateCalcSelect() {
    const select = document.getElementById('calc-item-select');
    if (!select) return;

    const sortedItems = [...items].sort((a, b) => a.itemId - b.itemId);
    select.innerHTML = sortedItems.map(item =>
        `<option value="${item.itemId}">${item.itemId} — ${item.itemName}</option>`
    ).join('');
}

const calcBtn = document.getElementById('calc-btn');
if (calcBtn) {
    calcBtn.addEventListener('click', async () => {
        const itemId = parseInt(document.getElementById('calc-item-select')?.value);
        const quantity = parseInt(document.getElementById('calc-quantity')?.value);

        if (!itemId) {
            alert('Выберите изделие');
            return;
        }

        if (!quantity || quantity <= 0) {
            alert('Введите корректное количество');
            return;
        }

        const requirements = {};

        function collect(nodeId, multiplier) {
            const children = boms.filter(b => b.parentId === nodeId);
            for (const child of children) {
                const compId = child.componentId;
                requirements[compId] = (requirements[compId] || 0) + child.quantity * multiplier;
                collect(compId, multiplier * child.quantity);
            }
        }

        collect(itemId, quantity);

        const resultsDiv = document.getElementById('calc-results');
        if (!resultsDiv) return;

        if (Object.keys(requirements).length === 0) {
            resultsDiv.innerHTML = '<p>Нет потребности в компонентах</p>';
            return;
        }

        let html = '<h4>Потребность:</h4>';
        for (const [compId, totalQty] of Object.entries(requirements)) {
            const item = items.find(i => i.itemId === parseInt(compId));
            const stockQty = Math.floor(item ? item.currentQuantity : 0);
            const shortage = totalQty - stockQty;

            if (shortage > 0) {
                html += `
                    <div class="result-item">
                        <span>${item ? item.itemName : 'Неизвестно'} (ID ${compId})</span>
                        <span>
                            <strong>${totalQty}</strong> 
                            <span class="badge shortage">недостаток: ${shortage}</span>
                        </span>
                    </div>
                `;
            } else {
                html += `
                    <div class="result-item">
                        <span>${item ? item.itemName : 'Неизвестно'} (ID ${compId})</span>
                        <span>
                            <strong>${totalQty}</strong> 
                            <span class="badge sufficient">в наличии: ${stockQty}</span>
                        </span>
                    </div>
                `;
            }
        }
        resultsDiv.innerHTML = html;
    });
}

// ==================== Склад ====================

async function loadWarehouseData() {
    await loadStock();
    await loadOperations();
}

async function loadStock() {
    try {
        const res = await fetch(`${API}/Warehouse/stock`);
        if (!res.ok) throw new Error(`Ошибка загрузки остатков: ${res.status}`);
        const stock = await res.json();

        // Пересчитываем общую стоимость для каждого элемента
        const stockWithTotal = stock.map(item => ({
            ...item,
            totalValue: item.currentQuantity * item.purchasePrice
        }));

        stockWithTotal.sort((a, b) => a.itemId - b.itemId);

        const tbody = document.getElementById('stock-table');
        if (!tbody) return;

        tbody.innerHTML = stockWithTotal.map(item => `
             <tr>
                <td><strong>${item.itemId}</strong></td>
                <td>${item.itemName}</td>
                <td>${Math.floor(item.currentQuantity)}</td>
                <td>${item.purchasePrice.toFixed(2)}</td>
                <td>${(item.currentQuantity * item.purchasePrice).toFixed(2)}</td>
             </tr>
        `).join('');
    } catch (err) {
        alert(err.message);
    }
}

async function loadOperations() {
    try {
        const res = await fetch(`${API}/Warehouse/operations`);
        if (!res.ok) throw new Error(`Ошибка загрузки операций: ${res.status}`);
        const operations = await res.json();

        const tbody = document.getElementById('operations-table');
        if (!tbody) return;

        tbody.innerHTML = operations.map(op => {
            const typeNames = {
                'Income': 'Приход',
                'Expense': 'Расход',
                'Adjustment': 'Корректировка'
            };
            const typeClass = op.operationType === 'Income' ? 'income' :
                op.operationType === 'Expense' ? 'expense' : 'adjustment';
            const sign = op.operationType === 'Income' ? '+' :
                op.operationType === 'Expense' ? '-' : '';

            return `
                 <tr>
                    <td>${new Date(op.createdAt).toLocaleString('ru-RU')}</td>
                    <td>${op.itemName} (ID ${op.itemId})</td>
                    <td><span class="badge ${typeClass}">${typeNames[op.operationType]}</span></td>
                    <td>${sign}${op.quantity}</td>
                    <td>${op.unitPrice.toFixed(2)}</td>
                    <td>${op.comment || '—'}</td>
                 </tr>
            `;
        }).join('');
    } catch (err) {
        alert(err.message);
    }
}

function initOperationModal() {
    const modal = document.getElementById('operation-modal');
    const openBtn = document.getElementById('add-operation-btn');
    const closeBtn = document.getElementById('operation-modal-close');
    const cancelBtn = document.getElementById('operation-modal-cancel');
    const form = document.getElementById('operation-form');

    if (openBtn) {
        openBtn.addEventListener('click', () => {
            document.getElementById('operation-modal-title').textContent = 'Новая операция';
            document.getElementById('op-type').value = 'Income';
            document.getElementById('op-quantity').value = '';
            document.getElementById('op-price').value = '';
            document.getElementById('op-comment').value = '';

            const sortedItems = [...items].sort((a, b) => a.itemId - b.itemId);
            const itemSelect = document.getElementById('op-item');
            if (itemSelect) {
                itemSelect.innerHTML = sortedItems.map(item =>
                    `<option value="${item.itemId}">${item.itemId} — ${item.itemName} (остаток: ${Math.floor(item.currentQuantity)})</option>`
                ).join('');
            }

            modal.classList.add('active');
        });
    }

    const closeModal = () => modal.classList.remove('active');
    if (closeBtn) closeBtn.addEventListener('click', closeModal);
    if (cancelBtn) cancelBtn.addEventListener('click', closeModal);

    if (form) {
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const data = {
                ItemId: parseInt(document.getElementById('op-item').value),
                OperationType: document.getElementById('op-type').value,
                Quantity: parseInt(document.getElementById('op-quantity').value),
                UnitPrice: parseFloat(document.getElementById('op-price').value),
                Comment: document.getElementById('op-comment').value
            };

            try {
                const response = await fetch(`${API}/Warehouse/operations`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(data)
                });
                if (!response.ok) {
                    const errorText = await response.text();
                    throw new Error(`Ошибка ${response.status}: ${errorText}`);
                }
                closeModal();

                // После проведения операции обновляем все данные
                await loadItems(); // Обновляем остатки в номенклатуре
                await loadBoms(); // Обновляем BOM для калькулятора
                await loadWarehouseData(); // Обновляем складские данные

                // Если активен калькулятор, обновляем результаты
                if (document.getElementById('calculator-view').classList.contains('active')) {
                    const calcBtn = document.getElementById('calc-btn');
                    if (calcBtn) calcBtn.click();
                }
            } catch (err) {
                alert('Не удалось сохранить операцию: ' + err.message);
            }
        });
    }
}
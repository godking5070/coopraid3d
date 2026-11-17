"use strict";
const buttons=[...document.querySelectorAll('[data-filter]')];const tiles=[...document.querySelectorAll('[data-category]')];buttons.forEach(button=>button.addEventListener('click',()=>{buttons.forEach(peer=>peer.setAttribute('aria-pressed',String(peer===button)));tiles.forEach(tile=>{tile.hidden=button.dataset.filter!=='All'&&tile.dataset.category!==button.dataset.filter;});}));

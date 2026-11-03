/**
=========================================================================
=========================================================================
Template Name: Able Pro - Bootstrap Admin Template
Author: Phoenixcoded
Support: https://phoenixcoded.authordesk.app
File: script.js
Description:  this file will contains code for handling Buynow link.
=========================================================================
=========================================================================
*/

var BuyNowLink = '';
var isp = '';
var ref = "";

function getQueryStringParameters() {
  const queryString = window.location.search;
  const params = new URLSearchParams(queryString);
  if (params.get('ref') == 'tf') {
    ref = "tf"
    sessionStorage.setItem('ref', ref);
  }
  check_isp();
  function check_isp() {
    if (sessionStorage.getItem('isp', isp)) {
      BuyNowLink = sessionStorage.getItem('BuyNowLink');
      var isp = sessionStorage.getItem('isp');
    } else {
      try {
        if (params.get('isp') == 1) {
          // is single product
          isp = '?isp=1';
          BuyNowLink = 'https://codedthemes.com/item/able-pro-bootstrap-admin-template/';
          if (ref == "tf") {
            BuyNowLink = 'https://1.envato.market/baeyGk';
          }
          sessionStorage.setItem('isp', isp);
          sessionStorage.setItem('BuyNowLink', BuyNowLink);
        } else {
          BuyNowLink = 'https://codedthemes.com/item/able-pro-dashboard-templates';
          if (ref == "tf") {
            BuyNowLink = 'https://1.envato.market/zNkqj6';
          }
        }
      } catch (err) {
        BuyNowLink = 'https://1.envato.market/zNkqj6';
      }
    }
  }

  document.addEventListener('DOMContentLoaded', function () {
    var elem = document.querySelectorAll('.btn-buy, .buynowlinks');
    for (var j = 0; j < elem.length; j++) {
      elem[j].setAttribute('href', BuyNowLink);
    }
  });
  document.addEventListener('DOMContentLoaded', function () {
    var elem = document.querySelectorAll('.technology-block a,.drp-technology a, .tech-link a');
    for (var j = 0; j < elem.length; j++) {
      var dattr = elem[j].getAttribute('href');
      elem[j].setAttribute('href', dattr + queryString);
    }
  });
}
getQueryStringParameters();

// =====================================
function changebrand(word) {
  // Map brand names to their corresponding classes
  const brandClasses = {
    Themeforest: 'tf',
    Codedthemes: 'ct' 
  };

  // Get the class to show based on the word
  const showClass = brandClasses[word] || null;
  if (!showClass) return;  // Exit if no matching brand

  // Select all elements for both classes
  const tfElements = document.querySelectorAll('.tf');
  const ctElements = document.querySelectorAll('.ct');

  // Helper to toggle display
  function toggleDisplay(elements, show) {
    elements.forEach(el => el.style.display = show ? '' : 'none');
  }

  // Show elements with the matched class, hide others
  toggleDisplay(tfElements, showClass === 'tf');
  toggleDisplay(ctElements, showClass === 'ct');
}

document.addEventListener('DOMContentLoaded', () => {
  if (ref === "tf") {
    changebrand('Themeforest');
  } else {
    changebrand('Codedthemes');
  }
});

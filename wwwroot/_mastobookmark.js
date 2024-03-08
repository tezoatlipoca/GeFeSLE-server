
function mastoBookmarkSaveDefaults() {
    console.debug('mastoBookmarkSaveDefaults');
    // get the values of the _mastobookmark.html form and save them to local storage
    let form = document.getElementById('mastobookmarkform');
    let formData = new FormData(form);
    let formObject = {};
    formData.forEach((value, key) => { formObject[key] = value });
    console.debug(' | formObject:', formObject);
    localStorage.setItem('mastobookmark', JSON.stringify(formObject));
}

function mastoBookmarkLoadDefaults() {
    console.debug('mastoBookmarkLoadDefaults');
    // load the values of the _mastobookmark.html form from local storage
    let form = document.getElementById('mastobookmarkform');
    let formObject = JSON.parse(localStorage.getItem('mastobookmark'));
    console.debug(' | formObject:', formObject);
    for (let key in formObject) {
        let element = form.elements[key];
        if (element) {
            element.value = formObject[key];
        }
    }
}

// when _mastobookmark.html loads run the above
window.onload = function () {
    console.debug('mastoBookmark - window.onload');
    mastoBookmarkLoadDefaults();

    // get the listId out of the querystring
    let urlParams = new URLSearchParams(window.location.search);
    let listId = urlParams.get('listId');


    console.debug(' | listId:', listId);
    // save it to the hidden field
    let listIdField = document.getElementById('listId');
    listIdField.value = listId;
    
}

async function mastodonGO(e){
    // prevent button default action
    e.preventDefault();
    console.debug('mastoBookmark - mastodonGO');
    
    // if we're in local mode, bail
    if(islocal()) {
        d('Cannot call API from a local file!');
        c(RC.BAD_REQUEST);
        return;
    }
    let loggedIn = await amLoggedIn();
    if(!loggedIn) {
        d('You must be logged in to do this!');
        c(RC.UNAUTHORIZED);
        return;
    }
    d('Processing...');
    c(RC.OK);
    
    // get the values of the _mastobookmark.html form and save them to local storage
    mastoBookmarkSaveDefaults();
    // get the listId out of the hidden field
    let listIdField = document.getElementById('listId');
    let listId = listIdField.value;

    // get num2Get and unbookmark bool from the form
    let num2Get = document.getElementById('num2Get').value;
    let unbookmark = document.getElementById('unbookmark').checked;
    // if unbookarm
    // build the url
    // get our current url and strip off _mastobookmark.html
    let url = window.location.href;
    url = url.replace(/_mastobookmark.html.*/, '');

    url = url + 'mastobookmarks/' + listId + '?num2Get=' + num2Get + '&unbookmark' + unbookmark;
    //redirect to the url
    window.location.replace(url);
    //alert('Redirecting to: ' + url);
}


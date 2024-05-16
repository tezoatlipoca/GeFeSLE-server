var gulp = require('gulp');

gulp.task('copy', function() {
    return gulp.src('node_modules/easymde/dist/*')
        .pipe(gulp.dest('wwwroot/lib/easymde/'));
});

// gulp.task('copy-emoji-js', function() {
//     return gulp.src('node_modules/emoji-js/lib/*')
//         .pipe(gulp.dest('wwwroot/lib/emoji_js/'));
// });

gulp.task('default', gulp.series('copy'));
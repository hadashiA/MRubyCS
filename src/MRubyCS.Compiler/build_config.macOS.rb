MRuby::CrossBuild.new("macos-arm64") do |conf|
  conf.toolchain :clang

  #conf.gem core: 'mruby-compiler'
  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '71749bc54a710ac4567130ebd1609c331ba60e7a'
  conf.gem './mrbgems/mrubycs-compiler'

  # Note: mruby 4.0 removed MRB_NO_PRESYM and `disable_presym`; presym is always on.
  conf.cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  conf.cc.flags << '-arch arm64'
  conf.linker.flags << '-arch arm64'
end

MRuby::CrossBuild.new("macos-x64") do |conf|
  conf.toolchain :clang

  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '71749bc54a710ac4567130ebd1609c331ba60e7a'
  conf.gem './mrbgems/mrubycs-compiler'

  # Note: mruby 4.0 removed MRB_NO_PRESYM and `disable_presym`; presym is always on.
  conf.cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  conf.cc.flags << '-arch x86_64'
  conf.linker.flags << '-arch x86_64'
end

MRuby::CrossBuild.new("macos-arm64") do |conf|
  conf.toolchain :clang

  #conf.gem core: 'mruby-compiler'
  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '375f6d8914399ff88a86dcac8e3104760f8a8c58'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  conf.cc.flags << '-arch arm64'
  conf.linker.flags << '-arch arm64'
end

MRuby::CrossBuild.new("macos-x64") do |conf|
  conf.toolchain :clang

  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '375f6d8914399ff88a86dcac8e3104760f8a8c58'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  conf.cc.flags << '-arch x86_64'
  conf.linker.flags << '-arch x86_64'
end
